/// <summary>
/// Calculates pricing totals for a single Agreement Booking Incident.
///
/// This method performs the following:
/// 1. Retrieves the Incident record.
/// 2. Determines how many Major and Minor visits occur during the agreement.
/// 3. Retrieves all child Products and Services for this Incident.
/// 4. Calculates the price/cost for a single visit.
/// 5. Multiplies those values by the number of Major or Minor visits.
/// 6. Updates the Incident with the calculated totals.
/// </summary>

using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FieldService
{
    public class AgreementBookingIncidentPricing : IPlugin
    {
        private const string IncidentTable =
            "msdyn_agreementbookingincident";

        private const string ProductTable =
            "msdyn_agreementbookingproduct";

        private const string ServiceTable =
            "msdyn_agreementbookingservice";

        private const string IncidentLookup =
            "msdyn_agreementbookingincident";

        private const string ServiceFrequencyField =
            "tb_servicefrequency";

        private const string ServiceDurationField =
            "tb_serviceduration";

        private const string ServiceTypeField =
            "tb_servicetype";

        private const string ChildTotalSellField =
            "tb_totalsellprice";

        private const string ChildTotalCostField =
            "tb_totalcost";

        // Parent Incident fields — replace with your actual schema names.
        private const string ProductTotalPriceField =
            "tb_productstotalprice";

        private const string ProductTotalCostField =
            "tb_productstotalcost";

        private const string ServiceTotalPriceField =
            "tb_servicetotalprice";

        private const string ServiceTotalCostField =
            "tb_servicetotalcost";

        private const string PerMajorField =
            "tb_permajor";

        private const string PerMinorField =
            "tb_perminor";

        private const string TotalSellField =
            "tb_totalprice";

        private const string TotalCostField =
            "tb_totalcost";

        private const string MarginAmountField =
            "tb_marginamount";

        private const string MarginPercentageField =
            "tb_marginpercentage";

        private const string MajorQuantityField =
            "tb_numberofmajors";

        private const string MinorQuantityField =
            "tb_numberofminors";

        private const int ServiceTypeMajor = 126700000;
        private const int ServiceTypeMinor = 126700001;

        private const int FrequencyAnnual = 126700000;
        private const int FrequencySemiAnnual = 126700001;
        private const int FrequencyQuarterly = 126700002;
        private const int FrequencyMonthly = 126700003;

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService =
                (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));

            IPluginExecutionContext context =
                (IPluginExecutionContext)serviceProvider.GetService(
                    typeof(IPluginExecutionContext));

            IOrganizationServiceFactory serviceFactory =
                (IOrganizationServiceFactory)serviceProvider.GetService(
                    typeof(IOrganizationServiceFactory));

            IOrganizationService service =
                serviceFactory.CreateOrganizationService(context.UserId);

            try
            {
                tracingService.Trace( "AgreementBookingIncidentPricing started. " + "Message={0}, Entity={1}, Stage={2}, Depth={3}", context.MessageName,context.PrimaryEntityName,context.Stage,context.Depth);

                if (context.Depth > 2)
                {
                    tracingService.Trace("Exiting because Depth > 2.");
                    return;
                }

                Guid incidentId = GetIncidentId(context, tracingService);

                if (incidentId == Guid.Empty)
                {
                    tracingService.Trace("Agreement Booking Incident ID was not found.");

                    return;
                }

                CalculateIncidentTotals(incidentId,service, tracingService);
            }
            catch (Exception ex)
            {
                tracingService.Trace( "AgreementBookingIncidentPricing exception: {0}", ex);

                throw new InvalidPluginExecutionException("An error occurred while calculating Agreement Booking Incident pricing.", ex);
            }
        }

        private static Guid GetIncidentId(IPluginExecutionContext context,ITracingService tracingService)
        {
            /*
             * Plug-in registered directly on Incident Update.
             */
            if (string.Equals( context.PrimaryEntityName, IncidentTable,StringComparison.OrdinalIgnoreCase))
            {
                if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity incidentTarget)
                {
                    return incidentTarget.Id;
                }

                return context.PrimaryEntityId;
            }

            /*
             * Product or Service Delete:
             * Read the Incident lookup from PreImage.
             */
            if (string.Equals(context.MessageName, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                if (context.PreEntityImages.Contains("PreImage"))
                {
                    Entity preImage =
                        context.PreEntityImages["PreImage"];

                    return preImage
                        .GetAttributeValue<EntityReference>(
                            IncidentLookup)
                        ?.Id ?? Guid.Empty;
                }

                tracingService.Trace("PreImage was not found for Delete.");

                return Guid.Empty;
            }

            /*
             * Product or Service Create/Update:
             * Prefer PostImage.
             */
            if (context.PostEntityImages.Contains("PostImage"))
            {
                Entity postImage =  context.PostEntityImages["PostImage"];

                Guid postIncidentId =   postImage.GetAttributeValue<EntityReference>(IncidentLookup)?.Id ?? Guid.Empty;

                if (postIncidentId != Guid.Empty)
                {
                    return postIncidentId;
                }
            }

            /*
             * Fallback to Target.
             */
            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity target)
            {
                return target.GetAttributeValue<EntityReference>(IncidentLookup)?.Id ?? Guid.Empty;
            }

            return Guid.Empty;
        }

        private static void CalculateIncidentTotals(Guid incidentId, IOrganizationService service,ITracingService tracingService)
        {
            // Retrieve the current Agreement Booking Incident.
            //
            // We need:
            // 1. Service Frequency (Annual, Quarterly, Monthly, etc.)
            // 2. Service Duration (1–5 years)
            // 3. Service Type (Major or Minor)
            //
            // These three fields determine how many visits
            // occur over the life of the agreement.
            Entity incident = service.Retrieve(IncidentTable,incidentId, new ColumnSet(ServiceFrequencyField, ServiceDurationField,ServiceTypeField));

            OptionSetValue frequencyOption =incident.GetAttributeValue<OptionSetValue>( ServiceFrequencyField);

            OptionSetValue durationOption =incident.GetAttributeValue<OptionSetValue>(ServiceDurationField);

            OptionSetValue serviceTypeOption = incident.GetAttributeValue<OptionSetValue>(ServiceTypeField);

            if (frequencyOption == null ||  durationOption == null ||    serviceTypeOption == null)
            {
                tracingService.Trace("Service Frequency, Service Duration, or Service Type is missing.");

                return;
            }

            // Convert the Service Duration option set
            // into the number of years.
            //
            // Example:
            // "3 Years" -> 3

            int serviceYears =  MapServiceDurationOptionsetToYears(durationOption.Value);

            var quantity =  CalculateMajorMinorQty( tracingService, frequencyOption.Value,serviceYears);

            int noOfMajors = quantity.majorQty;
            int noOfMinors = quantity.minorQty;          

            var productTotals = GetChildTotals(   service,ProductTable,incidentId);

            var serviceTotals = GetChildTotals(  service,  ServiceTable,incidentId);

            // Child totals represent the price and cost for one visit.
            decimal productPricePerVisit =
                productTotals.TotalSell;

            decimal productCostPerVisit =
                productTotals.TotalCost;

            decimal servicePricePerVisit =
                serviceTotals.TotalSell;

            decimal serviceCostPerVisit =
                serviceTotals.TotalCost;

            // Total sell price for one Major or Minor visit.
            decimal perVisitSell =
                productPricePerVisit + servicePricePerVisit;

            // Total cost for one Major or Minor visit.
            decimal perVisitCost =
                productCostPerVisit + serviceCostPerVisit;

            decimal perMajor = 0;
            decimal perMinor = 0;

            int multiplier;

            if (serviceTypeOption.Value == ServiceTypeMajor)
            {
                multiplier = noOfMajors;
                perMajor = perVisitSell;
            }
            else if (serviceTypeOption.Value == ServiceTypeMinor)
            {
                multiplier = noOfMinors;
                perMinor = perVisitSell;
            }
            else
            {
                tracingService.Trace(  "Unsupported Service Type: {0}",  serviceTypeOption.Value);

                return;
            }

            // Multiply each per-visit value by the applicable service quantity.
            decimal productTotalPrice =   productPricePerVisit * multiplier;

            decimal productTotalCost = productCostPerVisit * multiplier;

            decimal serviceTotalPrice =  servicePricePerVisit * multiplier;

            decimal serviceTotalCost =   serviceCostPerVisit * multiplier;

            decimal totalSell =      productTotalPrice + serviceTotalPrice;

            decimal totalCost =     productTotalCost + serviceTotalCost;

            decimal marginAmount =   totalSell - totalCost;

            decimal marginPercentage =   totalSell == 0  ? 0: Math.Round(marginAmount / totalSell * 100,2, MidpointRounding.AwayFromZero);

            Entity incidentUpdate =   new Entity( IncidentTable,incidentId);

         //   incidentUpdate[MajorQuantityField] = noOfMajors;

         //   incidentUpdate[MinorQuantityField] =  noOfMinors;

            incidentUpdate[ProductTotalPriceField] =  new Money(productTotalPrice);

            incidentUpdate[ProductTotalCostField] =   new Money(productTotalCost);

            incidentUpdate[ServiceTotalPriceField] = new Money(serviceTotalPrice);

            incidentUpdate[ServiceTotalCostField] =  new Money(serviceTotalCost);

            incidentUpdate[TotalSellField] =         new Money(totalSell);

            incidentUpdate[TotalCostField] =        new Money(totalCost);

            incidentUpdate[MarginAmountField] =      new Money(marginAmount);

            incidentUpdate[MarginPercentageField] =     marginPercentage;

            incidentUpdate[PerMajorField] =   new Money(perMajor);

            incidentUpdate[PerMinorField] =  new Money(perMinor);

            service.Update(incidentUpdate);

            tracingService.Trace( "Agreement Booking Incident updated successfully.");
        }

        /// <summary>
        /// Retrieves all Products or Services
        /// for a single Agreement Booking Incident
        /// and returns the total Sell Price
        /// and Total Cost.
        ///
        /// Each child record already contains:
        ///
        /// tb_totalsellprice
        /// tb_totalcost
        ///
        /// No calculations are performed here.
        /// The method simply sums the values.
        /// </summary>
        private static (decimal TotalSell, decimal TotalCost)  GetChildTotals( IOrganizationService service,  string childTableName, Guid incidentId)
        {
            QueryExpression query = new QueryExpression(childTableName)
                {
                    ColumnSet = new ColumnSet(
                        ChildTotalSellField,
                        ChildTotalCostField)
                };

            query.Criteria.AddCondition(  IncidentLookup,  ConditionOperator.Equal, incidentId);

            EntityCollection records = service.RetrieveMultiple(query);

            decimal totalSell = 0;
            decimal totalCost = 0;

            foreach (Entity record in records.Entities)
            {
                totalSell +=  record.GetAttributeValue<Money>(ChildTotalSellField)?.Value ?? 0;

                totalCost +=  record.GetAttributeValue<Money>(ChildTotalCostField)?.Value ?? 0;
            }

            return ( totalSell, totalCost);
        }


        // Calculate how many Major and Minor visits
        // occur during the agreement.
        //
        // Example:
        //
        // Quarterly + 3 Years
        //
        // Major Visits = 3
        // Minor Visits = 9
        //
        // These values will later be used as multipliers.
        private static ( int majorQty, int minorQty) CalculateMajorMinorQty(ITracingService tracingService,int serviceFrequency, int serviceDuration)
        {
            switch (serviceFrequency)
            {
                case FrequencyAnnual:
                    return (serviceDuration, 0);

                case FrequencySemiAnnual:
                    return (serviceDuration, serviceDuration);

                case FrequencyQuarterly:
                    return (serviceDuration, serviceDuration * 3);

                case FrequencyMonthly:
                    /*
                     * Preserves your existing Agreement plug-in rule:
                     * monthly visits are all treated as Major.
                     */
                    return (serviceDuration * 12, 0);

                default:
                    tracingService.Trace("Unknown Service Frequency: {0}",serviceFrequency);

                    return ( 0, 0);
            }
        }

        private static int MapServiceDurationOptionsetToYears(int durationOptionSetValue)
        {
            switch (durationOptionSetValue)
            {
                case 126700000:
                    return 1;

                case 126700001:
                    return 2;

                case 126700002:
                    return 3;

                case 126700003:
                    return 4;

                case 126700004:
                    return 5;

                default:
                    return 0;
            }
        }
    }
}