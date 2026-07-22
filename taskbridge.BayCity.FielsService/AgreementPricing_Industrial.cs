//<summary>
//Agreement: This is a new plug-in calculates totals from agreement booking products, agreement booking services.
//</summary>
// Step 1: Retrieve all Agreement Booking Products and Services
// associated with the Agreement. These records contain the per-service
// pricing that will be rolled up to the Agreement level.
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Metadata;

namespace Taskbridge.BayCity.FielsService
{
    public class AgreementPricing_Industrial : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            tracingService.Trace("AgreementPricing_Industrial: Start. Message={0}, Stage={1}, Depth={2}", context.MessageName, context.Stage, context.Depth);
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
                if (!string.Equals(target.LogicalName, "msdyn_agreement", StringComparison.OrdinalIgnoreCase))
                    return;

                // On Update, Target often contains only changed fields.
                // We retrieve the full record to safely read required fields.
                var agreementId = target.Id;
                if (agreementId == Guid.Empty)
                {
                    tracingService.Trace("Target.Id is empty. Exiting.");
                    return;
                }

                GetTotals(agreementId, service, tracingService);

            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in Execute method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in the AgreementPricing_Industrial: {ex.Message}", ex);
            }
        }
        private const int ServiceTypeMajor = 126700000;
        private const int ServiceTypeMinor = 126700001;

        private void GetTotals(Guid agreementId, IOrganizationService service, ITracingService tracingService)
        {
            try
            {
                tracingService.Trace("Calculating totals for agreement: {0}", agreementId);

                // Step 1: Retrieve all Agreement Booking Products and Services
                // associated with the Agreement. These records contain the per-service
                // pricing that will be rolled up to the Agreement level.

                var agrProducts = Get_Agreementbooking_Products(agreementId, service);
                var agrServices = Get_Agreementbooking_Services(agreementId, service);

                // Step 2: Determine how many Major and Minor services should occur
                // over the life of the agreement based on:
                // - Service Frequency (Annual, Quarterly, etc.)
                // - Service Duration (1-5 years)
                // Example:
                // Quarterly + 3 Years = 3 Major Services + 9 Minor Services

                Entity ent = service.Retrieve("msdyn_agreement", agreementId, new ColumnSet("tb_servicefrequency", "tb_serviceduration"));

                OptionSetValue serviceFrequencyOption = ent.GetAttributeValue<OptionSetValue>("tb_servicefrequency");
                OptionSetValue serviceDurationOption = ent.GetAttributeValue<OptionSetValue>("tb_serviceduration");

                int noOfMajors = 0;
                int noOfMinors = 0;

                if (serviceFrequencyOption != null && serviceDurationOption != null)
                {


                    int serviceDuration = MapServiceDurationOptionsetToYears(
                        serviceDurationOption.Value,
                        tracingService);

                    var qty = CalculateMajorMinorQty(
                        tracingService,
                        serviceFrequencyOption.Value,
                        serviceDuration);

                    noOfMajors = qty.majorQty;
                    noOfMinors = qty.minorQty;
                }
                else
                {
                    tracingService.Trace("Service Frequency or Service Duration is missing.");
                    return;
                }


                // Container objects used to accumulate pricing and cost totals
                // separately for Major and Minor service types.

                var major = new ServiceTotals();
                var minor = new ServiceTotals();

                // Step 3: Sum all Agreement Booking Product pricing.
                // Products are categorized as Major or Minor based on the related
                // Agreement Booking Incident service type.
                foreach (var bookingProduct in agrProducts.Entities)
                {
                    if (bookingProduct.Contains("type.tb_servicetype"))
                    {
                        var serviceTypeValue = (bookingProduct.GetAttributeValue<AliasedValue>("type.tb_servicetype")?.Value as OptionSetValue)?.Value;

                        // int serviceTypeValue = serviceType.Value;
                        //totalTaxableAmount += GetTaxableAmountfromIncidentTypeProducts(service, incidentType.Id);
                        if (serviceTypeValue == ServiceTypeMajor) // Major Incident
                        {
                            major.ProductPrice += GetMoneyValue(bookingProduct, "tb_totalsellprice");
                            major.ProductCost += GetMoneyValue(bookingProduct, "tb_totalcost");

                        }
                        else if (serviceTypeValue == ServiceTypeMinor) // Minor Incident
                        {
                            minor.ProductPrice += GetMoneyValue(bookingProduct, "tb_totalsellprice");
                            minor.ProductCost += GetMoneyValue(bookingProduct, "tb_totalcost");
                        }
                    }
                }
                // Step 4: Sum all Agreement Booking Service pricing.
                // Services are categorized as Major or Minor based on the related
                // Agreement Booking Incident service type.
                foreach (var bookingService in agrServices.Entities)
                {
                    if (bookingService.Contains("type.tb_servicetype"))
                    {
                        var serviceTypeValue = (bookingService.GetAttributeValue<AliasedValue>("type.tb_servicetype")?.Value as OptionSetValue)?.Value;

                        //totalTaxableAmount += GetTaxableAmountfromIncidentTypeProducts(service, incidentType.Id);
                        if (serviceTypeValue == ServiceTypeMajor) // Major Incident
                        {
                            major.ServicePrice += GetMoneyValue(bookingService, "tb_totalsellprice");
                            major.ServiceCost += GetMoneyValue(bookingService, "tb_totalcost");
                        }
                        else if (serviceTypeValue == ServiceTypeMinor) // Minor Incident
                        {
                            minor.ServicePrice += GetMoneyValue(bookingService, "tb_totalsellprice");
                            minor.ServiceCost += GetMoneyValue(bookingService, "tb_totalcost");
                        }
                    }
                }
                // Update overall totals
                decimal agr_productsTotal = major.ProductPrice * noOfMajors + minor.ProductPrice * noOfMinors;

                decimal agr_servicesTotal = major.ServicePrice * noOfMajors + minor.ServicePrice * noOfMinors;

                decimal agr_productsTotal_Cost = major.ProductCost * noOfMajors + minor.ProductCost * noOfMinors;

                decimal agr_servicesTotal_Cost = major.ServiceCost * noOfMajors + minor.ServiceCost * noOfMinors;


                tracingService.Trace($"Totals: ProdPrice={agr_productsTotal}, ProdCost={agr_productsTotal_Cost}, SvcPrice={agr_servicesTotal}, SvcCost={agr_servicesTotal_Cost}");

                // Update quote record with totals (change field names to your actual quote fields)
                var update = new Entity("msdyn_agreement", agreementId)
                {
                    ["tb_productstotalprice"] = new Money(agr_productsTotal),
                    ["tb_productstotalcost"] = new Money(agr_productsTotal_Cost),
                    ["tb_servicetotalprice"] = new Money(agr_servicesTotal),
                    ["tb_servicetotalcost"] = new Money(agr_servicesTotal_Cost),
                    ["tb_originalagreementprice"] = agr_productsTotal + agr_servicesTotal,
                    ["tb_totalagreementprice"] = agr_productsTotal + agr_servicesTotal,
                    ["tb_calculateprice"] = false,
                    //  ["tb_permajor"] = new Money(major.ProductPrice + major.ServicePrice),
                    // ["tb_perminor"] = new Money(minor.ProductPrice + minor.ServicePrice),
                    // ["tb_majorproductstotalprice"] = new Money(major.ProductPrice),
                    // ["tb_minorproductstotalprice"] = new Money(minor.ProductPrice),
                    // ["tb_majorservicestotalprice"] = new Money(major.ServicePrice),
                    //["tb_minorservicestotalprice"] = new Money(minor.ServicePrice)
                };

                service.Update(update);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in GetTotals method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred while calculating  totals: {ex.Message}", ex);
            }
        }
        private class ServiceTotals
        {
            public decimal ProductPrice { get; set; }
            public decimal ProductCost { get; set; }
            public decimal ServicePrice { get; set; }
            public decimal ServiceCost { get; set; }
        }
        // Helper method to retrieve Money values
        private decimal GetMoneyValue(Entity entity, string attributeName)
        {
            return entity.Attributes.Contains(attributeName) ? ((Money)entity[attributeName]).Value : 0;
        }
        public EntityCollection Get_Agreementbooking_Products(Guid id, IOrganizationService service)
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
        public EntityCollection Get_Agreementbooking_Services(Guid id, IOrganizationService service)
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

        // Replace these with your actual tb_servicefrequency option values
        private const int FrequencyAnnual = 126700000;
        private const int FrequencySemiAnnual = 126700001;
        private const int FrequencyQuarterly = 126700002;
        private const int FrequencyMonthly = 126700003;

        /// <summary>
        /// Calculates the number of Major and Minor service visits
        /// for the entire agreement term.
        ///
        /// Examples:
        /// Annual, 3 Years     => 3 Major, 0 Minor
        /// Semi Annual, 3 Years => 3 Major, 3 Minor
        /// Quarterly, 3 Years  => 3 Major, 9 Minor
        /// Monthly, 3 Years    => 36 Major, 0 Minor
        /// </summary>
        private (int majorQty, int minorQty) CalculateMajorMinorQty(ITracingService tracingService, int serviceFrequency, int serviceDuration)
        {
            try
            {

                // int majorServiceInterval = 12; // Default to annual
                // int minorServiceInterval = 0;

                switch (serviceFrequency)
                {
                    case FrequencyAnnual:
                        return (serviceDuration, 0);

                    case FrequencySemiAnnual:
                        return (serviceDuration, serviceDuration);

                    case FrequencyQuarterly:
                        return (serviceDuration, serviceDuration * 3);

                    case FrequencyMonthly:
                        return (serviceDuration * 12, 0);

                    default:
                        tracingService.Trace($"Unknown service frequency: {serviceFrequency}");
                        return (0, 0);
                }

                // int totalMajorServices = serviceDuration * (12 / majorServiceInterval); // Calculate the total number of major services
                // int totalMinorServices = (minorServiceInterval > 0) ? serviceDuration * (12 / minorServiceInterval) : 0; // Calculate the total number of minor services if applicable

                // return (totalMajorServices, totalMinorServices);


            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in CalculateMajorMinorQty method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in while calculating Major MinorService Qty : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Converts the Service Duration option set value
        /// into the corresponding number of years.
        /// </summary>
        private int MapServiceDurationOptionsetToYears(int durationOptionSetValue, ITracingService tracingService)
        {
            try
            {
                // Map the OptionSetValue to the corresponding number of years
                switch (durationOptionSetValue)
                {
                    case 126700000: // Example value for 1 year
                        return 1;
                    case 126700001: // Example value for 2 years
                        return 2;
                    case 126700002: // Example value for 3 years
                        return 3;
                    case 126700003: // Example value for 4 years
                        return 4;
                    case 126700004: // Example value for 5 years
                        return 5;
                    default:
                        return 0;
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in MapServiceDurationOptionsetToYears method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in while Mapping ServiceDuration OptionSetValue to the corresponding number of years : {ex.Message}", ex);
            }
        }
    }
}
