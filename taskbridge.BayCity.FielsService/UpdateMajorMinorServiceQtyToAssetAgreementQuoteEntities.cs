using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FielsService
{
    public class UpdateMajorMinorServiceQtyToAssetAgreementQuoteEntities : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            try
            {
                if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
                {
                    Entity target = (Entity)context.InputParameters["Target"];

                    if (target.LogicalName == "msdyn_customerasset" || target.LogicalName == "msdyn_agreement" || target.LogicalName == "tb_agreementquote")
                    {
                        Entity ent = service.Retrieve(target.LogicalName, target.Id, new ColumnSet(true));

                        OptionSetValue serviceFrequencyOption = ent.GetAttributeValue<OptionSetValue>("tb_servicefrequency");
                        OptionSetValue serviceDurationOption = ent.GetAttributeValue<OptionSetValue>("tb_serviceduration");

                        if (serviceFrequencyOption != null && serviceDurationOption != null)
                        {
                            string serviceFrequency = GetServiceFrequencyOptionSetText(service, ent.LogicalName, "tb_servicefrequency", serviceFrequencyOption.Value, tracingService);
                            int serviceDuration = MapServiceDurationOptionsetToYears(serviceDurationOption.Value, tracingService);
                            CalculateMajorMinorQty(service, tracingService, ent, serviceFrequency, serviceDuration);

                        }
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in Execute method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in the UpdateMajorMinorServiceQty plugin: {ex.Message}", ex);
            }
        }

         private void CalculateMajorMinorQty(IOrganizationService service, ITracingService tracingService, Entity entity, string serviceFrequency, int serviceDuration)
        {
            try
            {

                int majorServiceInterval = 12; // Default to annual
                int minorServiceInterval = 0;

                switch (serviceFrequency.ToLower())
                {
                    case "annual":
                        majorServiceInterval = 12;
                        break;
                    case "semi annual":
                        majorServiceInterval = 12;
                        minorServiceInterval = 12;
                        break;
                    case "quarterly":
                        majorServiceInterval = 12;
                        minorServiceInterval = 4;
                        break;
                    case "monthly":
                        majorServiceInterval = 1; // Monthly
                        break;
                    default:
                        tracingService.Trace($"Unknown service frequency: {serviceFrequency}");
                        return;
                }

                int totalMajorServices = serviceDuration * (12 / majorServiceInterval); // Calculate the total number of major services
                int totalMinorServices = (minorServiceInterval > 0) ? serviceDuration * (12 / minorServiceInterval) : 0; // Calculate the total number of minor services if applicable

                Entity e = new Entity(entity.LogicalName);

                e.Id = entity.Id;

                e["tb_ofmajorservices"] = totalMajorServices;

                e["tb_ofminorservices"] = totalMinorServices;

                service.Update(e);


            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in CalculateMajorMinorQty method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in while calculating Major MinorService Qty : {ex.Message}", ex);
            }
        }
      private string GetServiceFrequencyOptionSetText(IOrganizationService service, string entityLogicalName, string attributeLogicalName, int optionSetValue, ITracingService tracingService)
      {
            try
            {

                var attributeRequest = new RetrieveAttributeRequest
                {
                    EntityLogicalName = entityLogicalName,
                    LogicalName = attributeLogicalName,
                    RetrieveAsIfPublished = true
                };

                var attributeResponse = (RetrieveAttributeResponse)service.Execute(attributeRequest);
                var attributeMetadata = (PicklistAttributeMetadata)attributeResponse.AttributeMetadata;

                foreach (var option in attributeMetadata.OptionSet.Options)
                {
                    if (option.Value == optionSetValue)
                    {
                        return option.Label.UserLocalizedLabel.Label;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in GetServiceFrequencyOptionSetText method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in while getting an option set text : {ex.Message}", ex);
            }
        }

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
