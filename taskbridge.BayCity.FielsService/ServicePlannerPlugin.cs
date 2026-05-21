using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace taskbridge.BayCity.FielsService
{
    public class ServicePlannerPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            if (context.MessageName.ToLower() == "update" && context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
            {
                Entity target = (Entity)context.InputParameters["Target"];

                if (target.LogicalName == "tb_agreementquote")
                {
                    Entity asset = service.Retrieve("tb_agreementquote", target.Id, new ColumnSet(true));

                    bool createplanneritems = asset.GetAttributeValue<bool>("tb_createserviceplanner");
                    DateTime initialServiceDate = asset.GetAttributeValue<DateTime>("tb_initialservicedate");
                    OptionSetValue serviceFrequencyOption = asset.GetAttributeValue<OptionSetValue>("tb_servicefrequency");
                    OptionSetValue serviceDurationOption = asset.GetAttributeValue<OptionSetValue>("tb_serviceduration");

                    if (serviceFrequencyOption != null && serviceDurationOption != null&& createplanneritems is true)
                    {
                        string serviceFrequency = GetOptionSetText(service, "tb_agreementquote", "tb_servicefrequency", serviceFrequencyOption.Value);
                        int serviceDuration = MapDurationToYears(serviceDurationOption.Value);

                        GenerateServicePlannerRecords(service, asset, initialServiceDate, serviceFrequency, serviceDuration, tracingService);
                    }
                }
            }
        }
        private int MapDurationToYears(int durationOptionSetValue)
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

        private void GenerateServicePlannerRecords(IOrganizationService service, Entity aQuote, DateTime initialServiceDate, string serviceFrequency, int serviceDuration, ITracingService tracingService)
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
                    minorServiceInterval = 6;
                    break;
                case "quarterly":
                    majorServiceInterval = 12;
                    minorServiceInterval = 3;
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
            
          //  Guid agreementId = GetActiveAgreement(asset, service,tracingService);

            int serviceNumber = 1;
            for (int year = 0; year < serviceDuration; year++)
            {
                for (int month = 0; month < 12; month += minorServiceInterval > 0 ? minorServiceInterval : majorServiceInterval)
                {
                    Entity serviceRecord = new Entity("tb_assetserviceplanner");
                    serviceRecord["tb_relatedagreementquote"] = new EntityReference("tb_agreementquote", aQuote.Id);
                    serviceRecord["tb_servicenumber"] = serviceNumber;
                    serviceRecord["statuscode"] = new OptionSetValue(126700001); //

                    DateTime servicedate = initialServiceDate.AddMonths(year * 12 + month);

                    serviceRecord["tb_servicedate"] = servicedate;
                    serviceRecord["tb_contractyear"] = new OptionSetValue(year);// 0-Year 1, 1-Year 2,2-Year 3,3-Year 3,4-Yar 5

                    
                    serviceRecord["tb_schedulemonth"] = servicedate.ToString("MMM-yy").ToUpper();

                    //if (agreementId != Guid.Empty)
                    //{
                    //    serviceRecord["tb_agreement"] = new EntityReference("msdyn_agreement", agreementId);
                    //}

                    if (serviceFrequency.ToLower() == "semi annual")
                    {
                        serviceRecord["tb_servicetype"] = new OptionSetValue(serviceNumber % 2 == 1 ? 126700001 : 126700000); //  126700001 for Minor and 126700000 for Major
                    }
                    else if (serviceFrequency.ToLower() == "quarterly")
                    {
                        serviceRecord["tb_servicetype"] = new OptionSetValue(serviceNumber % 4 == 0 ? 126700000 : 126700001); // 126700001 for Minor and 126700000 for Major
                    }
                    else if (serviceFrequency.ToLower() == "monthly")
                    {
                        serviceRecord["tb_servicetype"] = new OptionSetValue(126700000); 
                    }
                    else
                    {
                        serviceRecord["tb_servicetype"] = new OptionSetValue(126700000);
                    }

                    service.Create(serviceRecord);
                    serviceNumber++;
                }
            }
        }

        private string GetOptionSetText(IOrganizationService service, string entityLogicalName, string attributeLogicalName, int optionSetValue)
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


        private Guid GetActiveAgreement(Entity asset, IOrganizationService service, ITracingService tracingService)
        {

            try
            {
                tracingService.Trace("get current active Agreement/ if multipe agreements available get the agreement without any service planner records");
                var fetchData = new
                {
                    msdyn_customerassetid = asset.Id
                };
                string fetchXml = $@"
                        <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                          <entity name='msdyn_agreement'>
                            <attribute name='msdyn_name' />
                            <attribute name='msdyn_systemstatus' />
                            <attribute name='msdyn_substatus' />
                            <attribute name='msdyn_serviceaccount' />
                            <attribute name='msdyn_enddate' />
                            <attribute name='msdyn_billingaccount' />
                            <attribute name='msdyn_startdate' />
                            <attribute name='msdyn_agreementid' />
                            <order attribute='msdyn_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='msdyn_systemstatus' operator='eq' value='690970001' />
                            </filter>
                            <link-entity name='tb_assetserviceplanner' from='tb_agreement' to='msdyn_agreementid' link-type='outer' alias='aq' />
                            <link-entity name='tb_msdyn_agreement_msdyn_customerasset' from='msdyn_agreementid' to='msdyn_agreementid' visible='false' intersect='true'>
                              <link-entity name='msdyn_customerasset' from='msdyn_customerassetid' to='msdyn_customerassetid' alias='ar'>
                                <filter type='and'>
                                  <condition attribute='msdyn_customerassetid' operator='eq' value='{fetchData.msdyn_customerassetid}' />
                                </filter>
                              </link-entity>
                            </link-entity>
                            <filter type='and'>
                              <condition entityname='aq' attribute='tb_agreement' operator='null' />
                            </filter>
                          </entity>
                        </fetch>";

                // Retrieve the records using the FetchXML
                EntityCollection activeAgreements = service.RetrieveMultiple(new FetchExpression(fetchXml));

             



                //var query_msdyn_systemstatus = 690970001; //active agreement;
                //var query_tb_msdyn_agreement_msdyn_customerasset_msdyn_customerassetid = asset.Id;

                //var query = new QueryExpression("msdyn_agreement");
                //query.ColumnSet.AddColumns("msdyn_systemstatus", "tb_agreementstatus", "msdyn_agreementid", "msdyn_name");
                //query.Criteria.AddCondition("msdyn_systemstatus", ConditionOperator.Equal, query_msdyn_systemstatus);
                //var query_tb_msdyn_agreement_msdyn_customerasset = query.AddLink(
                //    "tb_msdyn_agreement_msdyn_customerasset",
                //    "msdyn_agreementid",
                //    "msdyn_agreementid");
                //query_tb_msdyn_agreement_msdyn_customerasset.Columns.AddColumns(
                //    "msdyn_agreementid",
                //    "msdyn_customerassetid",
                //    "tb_msdyn_agreement_msdyn_customerassetid");
                //query_tb_msdyn_agreement_msdyn_customerasset.LinkCriteria.AddCondition("msdyn_customerassetid", ConditionOperator.Equal, query_tb_msdyn_agreement_msdyn_customerasset_msdyn_customerassetid);
                ////condition to make sure this agreement doesn't have any serviceplanner records.
                //var query_tb_assetserviceplanner = query.AddLink(   "tb_assetserviceplanner",
                //                                                    "msdyn_agreementid",
                //                                                    "tb_agreement",
                //                                                    JoinOperator.LeftOuter);
                //query_tb_assetserviceplanner.LinkCriteria.AddCondition("tb_agreement", ConditionOperator.Null);
                // EntityCollection activeAgreements = service.RetrieveMultiple(query);

                if (activeAgreements.Entities.Count>1)
                {
                    tracingService.Trace(""+activeAgreements.Entities.Count+"");
                    throw new InvalidPluginExecutionException($" Asset has multiple active agreements");
                }
                else if(activeAgreements.Entities.Count>0)
                {
                    return activeAgreements.Entities[0].Id;
                }
                return Guid.Empty;                
             
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in GetActiveAgreement method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred while retrieving active agreement: {ex.Message}", ex);
            }
        }
    }
}
