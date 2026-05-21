//<summary>
//Agreement Quote: This is a new plug-in calculates totals from agreement booking products, agreement booking services.
//</summary>

using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FielsService
{
    public class AgreementQuote_Pricing_V1 : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            tracingService.Trace("AgreementQuote_Pricing_V1: Start. Message={0}, Stage={1}, Depth={2}", context.MessageName, context.Stage, context.Depth);
            try
            {
                // Prevent recursion (because we update the same record)
                if (context.Depth > 1)
                {
                    tracingService.Trace("Exiting because Depth > 1 (recursion protection).");
                    return;
                }
                // Ensure the plugin is executed on Update and target exists
                if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase))
                    return;
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
                    return;
                if (!string.Equals(target.LogicalName, "tb_agreementquote", StringComparison.OrdinalIgnoreCase))
                    return;

                // On Update, Target often contains only changed fields.
                // We retrieve the full record to safely read required fields.
                var quoteId = target.Id;
                if (quoteId == Guid.Empty)
                {
                    tracingService.Trace("Target.Id is empty. Exiting.");
                    return;
                }

                var agreementQuote = service.Retrieve(
                    "tb_agreementquote",
                    quoteId,
                    new ColumnSet("tb_agreement", "tb_calculateprice", "tb_ofmajorservices", "tb_ofminorservices")
                );

                bool calculate = agreementQuote.GetAttributeValue<bool?>("tb_calculateprice") == true;
                //if (!calculate)
                //{
                //    tracingService.Trace("tb_calculateprice is not true. Exiting.");
                //    return;
                //}
                Guid agreementID = agreementQuote.GetAttributeValue<EntityReference>("tb_agreement").Id;

                if (agreementID == null || agreementID == Guid.Empty)
                {
                    tracingService.Trace("tb_agreement is missing/empty. Cannot calculate totals. Exiting.");
                    return;
                }
                      
                  GetTotals(agreementID, agreementQuote.Id, service, tracingService);                   
               
                
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in Execute method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in the AgreementQuote_Pricing plugin: {ex.Message}", ex);
            }
        }

        private void GetTotals( Guid agreementid, Guid agreementQuoteId, IOrganizationService service, ITracingService tracingService)
        {
            try
            {
                tracingService.Trace("Calculating totals for agreement quote: {0}", agreementQuoteId);

                 var agrProducts =  Get_Agreementbooking_Products(agreementid,service);
                 var agrServices =  Get_Agreementbooking_Services(agreementid,service);               

                // Read number of majors/minors from quote
                
                Entity agrQuote = service.Retrieve("tb_agreementquote", agreementQuoteId, new ColumnSet("tb_ofmajorservices", "tb_ofminorservices"));
                int noOfMajors = agrQuote.GetAttributeValue<int?>("tb_ofmajorservices") ?? 0;
                int noOfMinors = agrQuote.GetAttributeValue<int?>("tb_ofminorservices") ?? 0;


                decimal major_productsPrice = 0;  
                decimal major_servicesPrice = 0;    
                decimal major_productsCost = 0;
                decimal major_servicesCost = 0;

                decimal minor_productsPrice = 0;
                decimal minor_servicesPrice = 0;
                decimal minor_productsCost = 0;
                decimal minor_servicesCost = 0;

                // Loop through related products and calculate totals
                foreach (var bookingProduct in agrProducts.Entities)
                {
                    if (bookingProduct.Contains("type.tb_servicetype"))
                    {
                        var serviceTypeValue = (bookingProduct.GetAttributeValue<AliasedValue>("type.tb_servicetype")?.Value as OptionSetValue)?.Value;

                       // int serviceTypeValue = serviceType.Value;
                        //totalTaxableAmount += GetTaxableAmountfromIncidentTypeProducts(service, incidentType.Id);
                        if (serviceTypeValue == 126700000) // Major Incident
                        {
                            major_productsPrice += GetMoneyValue(bookingProduct, "tb_totalsellprice");                           
                            major_productsCost += GetMoneyValue(bookingProduct, "tb_totalcost");
                           
                        }
                        else if (serviceTypeValue == 126700001) // Minor Incident
                        {
                            minor_productsPrice += GetMoneyValue(bookingProduct, "tb_totalsellprice");                           
                            minor_productsCost += GetMoneyValue(bookingProduct, "tb_totalcost");                           
                        }
                    }
                }
                // Loop through related services and calculate totals
                foreach (var bookingService in agrServices.Entities)
                {
                    if (bookingService.Contains("type.tb_servicetype"))
                    {
                        var serviceTypeValue = (bookingService.GetAttributeValue<AliasedValue>("type.tb_servicetype")?.Value as OptionSetValue)?.Value;

                        //totalTaxableAmount += GetTaxableAmountfromIncidentTypeProducts(service, incidentType.Id);
                        if (serviceTypeValue == 126700000) // Major Incident
                        {                           
                            major_servicesPrice += GetMoneyValue(bookingService, "tb_totalsellprice");                           
                            major_servicesCost += GetMoneyValue(bookingService, "tb_totalcost");
                        }
                        else if (serviceTypeValue == 126700001) // Minor Incident
                        {                          
                            minor_servicesPrice += GetMoneyValue(bookingService, "tb_totalsellprice");                    
                            minor_servicesCost += GetMoneyValue(bookingService, "tb_totalcost");
                        }
                    }
                }
                // Update overall totals
                decimal quote_productsTotal = major_productsPrice * noOfMajors + minor_productsPrice * noOfMinors;
                decimal quote_servicesTotal = major_servicesPrice * noOfMajors + minor_servicesPrice * noOfMinors;
                decimal quote_productsTotal_Cost = major_productsCost * noOfMajors + minor_productsCost * noOfMinors;
                decimal quote_servicesTotal_Cost = major_servicesCost * noOfMajors + minor_servicesCost * noOfMinors;


                tracingService.Trace($"Totals: ProdPrice={quote_productsTotal}, ProdCost={quote_productsTotal_Cost}, SvcPrice={quote_servicesTotal}, SvcCost={quote_servicesTotal_Cost}");

                // Update quote record with totals (change field names to your actual quote fields)
                var update = new Entity("tb_agreementquote", agreementQuoteId)
                {
                    ["tb_productstotalprice"] = new Money(quote_productsTotal),
                    ["tb_productstotalcost"] = new Money(quote_productsTotal_Cost),
                    ["tb_servicetotalprice"] = new Money(quote_servicesTotal),
                    ["tb_servicetotalcost"] = new Money(quote_productsTotal_Cost),
                    ["tb_originalagreementprice"] = quote_productsTotal + quote_servicesTotal,
                    ["tb_calculateprice"] = false,
                    ["tb_permajor"] = major_productsPrice + major_servicesPrice,
                    ["tb_perminor"] = minor_productsPrice + minor_servicesPrice,
                    ["tb_majorproductstotalprice"]= major_productsPrice,
                    ["tb_minorproductstotalprice"] = minor_productsPrice,
                    ["tb_majorservicestotalprice"]= major_servicesPrice,
                    ["tb_minorservicestotalprice"] = minor_servicesPrice
                };

                service.Update(update);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in GetTotals method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred while calculating  totals: {ex.Message}", ex);
            }
        }
        // Helper method to retrieve Money values
        private decimal GetMoneyValue(Entity entity, string attributeName)
        {
            return entity.Attributes.Contains(attributeName) ? ((Money)entity[attributeName]).Value : 0;
        }
        public EntityCollection Get_Agreementbooking_Products(Guid id,IOrganizationService service)
        {
            var query_msdyn_agreement = id;

            var query = new QueryExpression("msdyn_agreementbookingproduct");
            query.ColumnSet.AddColumns(
                "msdyn_agreement",
                "msdyn_agreementbookingincident",
                "msdyn_agreementbookingproductid",
                "msdyn_agreementbookingsetup",
                "msdyn_product",
                "msdyn_qtytobill",
                "msdyn_quantity",
                "msdyn_unit",
                "msdyn_unitamount",
                "tb_totalsellprice",
                    "tb_totalcost");
            query.Criteria.AddCondition("msdyn_agreement", ConditionOperator.Equal, query_msdyn_agreement);
            // Link to Agreement Booking Incident to get service type
            var query_msdyn_agreementbookingincident = query.AddLink(
                "msdyn_agreementbookingincident",
                "msdyn_agreementbookingincident",
                "msdyn_agreementbookingincidentid");
            query_msdyn_agreementbookingincident.EntityAlias = "type";
            query_msdyn_agreementbookingincident.Columns.AddColumn("tb_servicetype");//major,minor

            return service.RetrieveMultiple(query);

        }
        public EntityCollection Get_Agreementbooking_Services(Guid id,IOrganizationService service)
        {
            var query_msdyn_agreement = id;

            var query = new QueryExpression("msdyn_agreementbookingservice");
            query.ColumnSet.AddColumns(
                "msdyn_agreement",
                "msdyn_agreementbookingincident",
                "msdyn_agreementbookingserviceid",
                "msdyn_agreementbookingsetup",
                "msdyn_service",
                "msdyn_duration",
                "msdyn_durationtobill",
                "msdyn_unit",
                "msdyn_unitamount",
                "tb_totalsellprice",
                    "tb_totalcost");
            query.Criteria.AddCondition("msdyn_agreement", ConditionOperator.Equal, query_msdyn_agreement);
            // Link to Agreement Booking Incident to get service type
            var query_msdyn_agreementbookingincident = query.AddLink(
                "msdyn_agreementbookingincident",
                "msdyn_agreementbookingincident",
                "msdyn_agreementbookingincidentid");
            query_msdyn_agreementbookingincident.EntityAlias = "type";
            query_msdyn_agreementbookingincident.Columns.AddColumn("tb_servicetype");//major, minor 

            return service.RetrieveMultiple(query);

        }
    }
}
