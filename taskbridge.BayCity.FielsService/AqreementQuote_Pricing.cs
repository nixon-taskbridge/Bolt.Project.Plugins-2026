//<summary>
//When creating a new agreement, users provide quotes to their customers.
// Multiple quotes can be created, but only one will be designated as the primary quote. 
//After creating the agreement, users attach assets to it. 
//Subsequently, a quote agreement with the same pricing and service fields as the agreement is created.
// AssociatedAssetsToAgreement plug-in is used to pull the associated assets already on the agreement into the quote 
// This Plug-in will provide their pricing. 
//The plug-in retrieves all major/minor incidents connected to the related assets. 
//The pricing is separated as a major incident products price/cost, major incident services price/cost, minor incident products price/cost, and minor incident services price/cost. 
//The reason for separate calculations is that if the number of major incidents changes, only major products and services need to be re-calculated. 
//Once the user designates the primary quote, the pricing information is transferred to the Service Details Section and Final Quote Details Section on the agreement level.
//</summary>

using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

public class AgreementQuote_Pricing : IPlugin
{
    decimal quote_productsTotal = 0.0m;
    decimal quote_servicesTotal = 0.0m;
    decimal quote_productsTotal_Cost = 0.0m;
    decimal quote_servicesTotal_Cost = 0.0m;
    decimal totalTaxableAmount = 0;
    public void Execute(IServiceProvider serviceProvider)
    {
        ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

        try
        {
            // Ensure the plugin is executed on Update and target exists
            if ((context.MessageName != "Update") || !context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
            {
                return;
            }

            // Retrieve the Agreement Quote record
            Entity agreementQuote = (Entity)context.InputParameters["Target"];

            // Only proceed if the calculate price field value is Yes
            if (agreementQuote.Contains("tb_calculateprice") && agreementQuote.GetAttributeValue<bool>("tb_calculateprice") is true)
            {
                tracingService.Trace("Calculate Price: True");

                // Call method to get and process assets
                GetAssets(service, agreementQuote.Id, tracingService);
            }
        }
        catch (Exception ex)
        {
            tracingService.Trace("Exception in Execute method: {0}", ex.ToString());
            throw new InvalidPluginExecutionException($"An error occurred in the AgreementQuote_Pricing plugin: {ex.Message}", ex);
        }
    }

    // Method to retrieve asset associations and calculate totals
    private void GetAssets(IOrganizationService service, Guid agreementQuoteId, ITracingService tracingService)
    {
        try
        {
            tracingService.Trace("Retrieving existing assets");

            // Query to retrieve asset associations to this agreement quote
            QueryExpression query = new QueryExpression("tb_msdyn_customerasset_tb_agreementquote");
            query.ColumnSet = new ColumnSet(true);
            query.Criteria.AddCondition("tb_agreementquoteid", ConditionOperator.Equal, agreementQuoteId);

            EntityCollection relatedAssociations = service.RetrieveMultiple(query);

            tracingService.Trace("Number of related assets found: {0}", relatedAssociations.Entities.Count);

            // Loop through each asset and calculate pricing
            foreach (Entity association in relatedAssociations.Entities)
            {
                tracingService.Trace("Processing asset: {0}", association.Id);

                GetAssetTotals(service, association.GetAttributeValue<Guid>("msdyn_customerassetid"), agreementQuoteId, tracingService);
            }

            if (relatedAssociations.Entities.Count>0)
                // Update the quote with calculated pricing
                UpdateQuotewithPricing(service, agreementQuoteId, tracingService);
        }
        catch (Exception ex)
        {
            tracingService.Trace("Exception in GetAssets method: {0}", ex.ToString());
            throw new InvalidPluginExecutionException($"An error occurred while retrieving asset associations: {ex.Message}", ex);
        }
    }

    // Method to calculate totals for an asset by retrieving related incidents
    private void GetAssetTotals(IOrganizationService service, Guid customerAssetId, Guid agreementQuoteId, ITracingService tracingService)
    {
        try
        {
            tracingService.Trace("Calculating totals for asset: {0}", customerAssetId);

            // Retrieve related incidents for the asset
            var incidentTypes = RetrieveAssociatedAssetIncidentTypes(service, customerAssetId, tracingService);

            decimal major_productsPrice = 0;
            decimal major_servicesPrice = 0;
            decimal major_productsCost = 0;
            decimal major_servicesCost = 0;

            decimal minor_productsPrice = 0;
            decimal minor_servicesPrice = 0;
            decimal minor_productsCost = 0;
            decimal minor_servicesCost = 0;

            
            //
            int noofMajors = 0;
            int noofMinors = 0;

            Entity agrQuote = service.Retrieve("tb_agreementquote", agreementQuoteId, new ColumnSet("tb_ofmajorservices", "tb_ofminorservices"));
            if (agrQuote.Attributes.Contains("tb_ofmajorservices"))
                noofMajors = agrQuote.GetAttributeValue<int>("tb_ofmajorservices");
            if (agrQuote.Attributes.Contains("tb_ofminorservices"))
                noofMinors = agrQuote.GetAttributeValue<int>("tb_ofminorservices");

            // Loop through related incidents and calculate totals
            foreach (var incidentType in incidentTypes.Entities)
            {
                if (incidentType.Contains("tb_servicetype"))
                {
                    var serviceType = (OptionSetValue)incidentType["tb_servicetype"];
                    int serviceTypeValue = serviceType.Value;
                    totalTaxableAmount += GetTaxableAmountfromIncidentTypeProducts(service,incidentType.Id);
                    if (serviceTypeValue == 126700000) // Major Incident
                    {
                        major_productsPrice += GetMoneyValue(incidentType, "tb_productstotal");
                        major_servicesPrice += GetMoneyValue(incidentType, "tb_servicestotal");
                        major_productsCost += GetMoneyValue(incidentType, "tb_productstotalcost");
                        major_servicesCost += GetMoneyValue(incidentType, "tb_servicestotalcost");
                    }
                    else if (serviceTypeValue == 126700001) // Minor Incident
                    {
                        minor_productsPrice += GetMoneyValue(incidentType, "tb_productstotal");
                        minor_servicesPrice += GetMoneyValue(incidentType, "tb_servicestotal");
                        minor_productsCost += GetMoneyValue(incidentType, "tb_productstotalcost");
                        minor_servicesCost += GetMoneyValue(incidentType, "tb_servicestotalcost");
                    }
                }
            }

            // Update overall totals
            quote_productsTotal += major_productsPrice * noofMajors + minor_productsPrice * noofMinors;
            quote_servicesTotal += major_servicesPrice * noofMajors + minor_servicesPrice * noofMinors;
            quote_productsTotal_Cost += major_productsCost * noofMajors + minor_productsCost * noofMinors;
            quote_servicesTotal_Cost += major_servicesCost * noofMajors + minor_servicesCost * noofMinors;
        }
        catch (Exception ex)
        {
            tracingService.Trace("Exception in GetAssetTotals method: {0}", ex.ToString());
            throw new InvalidPluginExecutionException($"An error occurred while calculating asset totals: {ex.Message}", ex);
        }
    }

    // Helper method to retrieve Money values
    private decimal GetMoneyValue(Entity entity, string attributeName)
    {
        return entity.Attributes.Contains(attributeName) ? ((Money)entity[attributeName]).Value : 0;
    }

    // Method to retrieve incidents associated with the asset
    private EntityCollection RetrieveAssociatedAssetIncidentTypes(IOrganizationService service, Guid customerAssetId, ITracingService tracingService)
    {
        try
        {
            tracingService.Trace("Retrieving associated incident types for asset: {0}", customerAssetId);

            var query = new QueryExpression("msdyn_incidenttype")
            {
                ColumnSet = new ColumnSet("tb_servicetype", "tb_estimatetotal", "tb_productstotal", "tb_servicestotal", "tb_productstotalcost", "tb_servicestotalcost")
            };

            var link = query.AddLink("tb_incidenttype_customerasset", "msdyn_incidenttypeid", "msdyn_incidenttypeid", JoinOperator.Inner);
            link.LinkCriteria.AddCondition("msdyn_customerassetid", ConditionOperator.Equal, customerAssetId);

            return service.RetrieveMultiple(query);
        }
        catch (Exception ex)
        {
            tracingService.Trace("Exception in RetrieveAssociatedAssetIncidentTypes method: {0}", ex.ToString());
            throw new InvalidPluginExecutionException($"An error occurred while retrieving associated incident types: {ex.Message}", ex);
        }
    }

    // Method to update the Agreement Quote with calculated totals
    private void UpdateQuotewithPricing(IOrganizationService service, Guid agreementQuoteId, ITracingService tracingService)
    {
        try
        {
            tracingService.Trace("Updating quote with calculated pricing");

            Entity quote = new Entity("tb_agreementquote", agreementQuoteId);
            quote["tb_productstotalprice"] = quote_productsTotal;
            quote["tb_servicetotalprice"] = quote_servicesTotal;
            quote["tb_productstotalcost"] = quote_productsTotal_Cost;
            quote["tb_servicetotalcost"] = quote_servicesTotal_Cost;
            quote["tb_originalagreementprice"] = quote_productsTotal + quote_servicesTotal;
            quote["tb_calculateprice"] = false;
            quote["tb_taxamount"] = totalTaxableAmount;
            service.Update(quote);
        }
        catch (Exception ex)
        {
            tracingService.Trace("Exception in UpdateQuotewithPricing method: {0}", ex.ToString());
            throw new InvalidPluginExecutionException($"An error occurred while updating quote pricing: {ex.Message}", ex);
        }
    }
    //indicent type products has a field 
    private decimal GetTaxableAmountfromIncidentTypeProducts(IOrganizationService service,Guid incidentTypeId)
    {

        var query_tb_taxable = 126700000;
        var query_msdyn_incidenttype = incidentTypeId;

        var query = new QueryExpression("msdyn_incidenttypeproduct");
        query.ColumnSet.AddColumns("msdyn_product", "statecode", "tb_taxable", "tb_total");
        query.Criteria.AddCondition("tb_taxable", ConditionOperator.Equal, query_tb_taxable);
        query.Criteria.AddCondition("msdyn_incidenttype", ConditionOperator.Equal, query_msdyn_incidenttype);

        EntityCollection incidentProducts =  service.RetrieveMultiple(query);

        decimal taxableAmount = 0;
        foreach(var ipro in incidentProducts.Entities)
        {
          taxableAmount +=  GetMoneyValue(ipro, "tb_total");
        }

        return taxableAmount;

    }
}
