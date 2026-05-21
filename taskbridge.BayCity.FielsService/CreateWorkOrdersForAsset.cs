using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FielsService
{
    public class CreateWorkOrdersForAsset : IPlugin
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

                //If user perform "Create WorkOrder" from Asset Form
                if (target.LogicalName == "msdyn_customerasset")
                {
                    tracingService.Trace("Create work order is marked as Yes on the Asset Form.");
                    Entity assetEntity = service.Retrieve("msdyn_customerasset", target.Id, new ColumnSet(true));

                    if (assetEntity.Attributes.Contains("tb_createworkorders") && assetEntity.GetAttributeValue<bool>("tb_createworkorders") is true)
                    {
                        DateTime initialServiceDate = assetEntity.GetAttributeValue<DateTime>("tb_initialservicedate");
                        OptionSetValue serviceFrequencyOption = assetEntity.GetAttributeValue<OptionSetValue>("tb_servicefrequency");
                        OptionSetValue serviceDurationOption = assetEntity.GetAttributeValue<OptionSetValue>("tb_serviceduration");

                        if (serviceFrequencyOption != null && serviceDurationOption != null)
                        {
                            string serviceFrequency = GetOptionSetText(service, "msdyn_customerasset", "tb_servicefrequency", serviceFrequencyOption.Value);
                            int serviceDuration = MapDurationToYears(serviceDurationOption.Value);

                            CreateWorkOrderSchedule(service, assetEntity, initialServiceDate, serviceFrequency, serviceDuration, tracingService);
                        }
                       
                    }

                }
            }
        }
        private void CreateWorkOrderSchedule(IOrganizationService service, Entity asset, DateTime initialServiceDate, string serviceFrequency, int serviceDuration, ITracingService tracingService)
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

            Guid agreementId = GetActiveAgreement(asset, service, tracingService);

            int serviceNumber = 1;
            for (int year = 0; year < serviceDuration; year++)
            {
                for (int month = 0; month < 12; month += minorServiceInterval > 0 ? minorServiceInterval : majorServiceInterval)
                {
                     int workorderType = 0;
                    if (serviceFrequency.ToLower() == "semi annual")
                    {
                        workorderType = serviceNumber % 2 == 1 ? 126700001 : 126700000; //  126700001 for Minor and 126700000 for Major
                    }
                    else if (serviceFrequency.ToLower() == "quarterly")
                    {
                        workorderType = serviceNumber % 4 == 0 ? 126700000 : 126700001; // 126700001 for Minor and 126700000 for Major
                    }
                    else if (serviceFrequency.ToLower() == "monthly")
                    {
                        workorderType = 126700000;
                    }
                    else
                    {
                        workorderType = 126700000;
                    }
                    DateTime servicedate = initialServiceDate.AddMonths(year * 12 + month);

                    CreateWorkOrder(asset,agreementId, workorderType, service,tracingService,servicedate);
                    serviceNumber++;
                }
            }
        }
        public void CreateWorkOrder(Entity asset,Guid agreementId, int workorderType, IOrganizationService service, ITracingService tracingService,DateTime servicedate)
        {
            try
            {
                
                Guid assetID = asset.Id;

                tracingService.Trace($"Fetching asset details for Asset ID: {assetID}");
               // Entity assetDetails = service.Retrieve("msdyn_customerasset", assetID, new ColumnSet(true));

                var incidentTypeID = GetIncidentTypeIdFromAsset(assetID, workorderType, service, tracingService);

                Entity workOrder = new Entity("msdyn_workorder");

                workOrder["tb_activitystages"] = new OptionSetValue(126700000); //inactive
                workOrder["msdyn_systemstatus"] = new OptionSetValue(690970000);//unscheduled
                

                if (workorderType == 126700000)
                {
                    workOrder["msdyn_workordertype"] = new EntityReference("msdyn_workordertype", new Guid("4eaecf81-8bb1-ef11-b8e9-000d3a1ec6a6"));
                    workOrder["msdyn_substatus"] = new EntityReference("msdyn_workordersubstatus", new Guid("f0536ec9-8bb1-ef11-b8e9-000d3a1ec6a6"));//parts needed

                }
                else if (workorderType == 126700001)
                {
                    workOrder["msdyn_workordertype"] = new EntityReference("msdyn_workordertype", new Guid("52aecf81-8bb1-ef11-b8e9-000d3a1ec6a6"));
                    workOrder["msdyn_substatus"] = new EntityReference("msdyn_workordersubstatus", new Guid("fa536ec9-8bb1-ef11-b8e9-000d3a1ec6a6"));//ready to schedule
                }

                if (asset.Contains("msdyn_account"))
                {
                    Guid accountId = asset.GetAttributeValue<EntityReference>("msdyn_account").Id;
                    workOrder["msdyn_billingaccount"] = new EntityReference("account", accountId);
                    workOrder["msdyn_serviceaccount"] = new EntityReference("account", accountId);
                    Entity  account= service.Retrieve("account", accountId, new ColumnSet("msdyn_salestaxcode"));
                    
                    if (account.Attributes.Contains("msdyn_salestaxcode"))
                    {
                        Guid salestaxcodeId = account.GetAttributeValue<EntityReference>("msdyn_salestaxcode").Id;
                        tracingService.Trace($"Taxcode: {salestaxcodeId}");
                        if (salestaxcodeId != null)
                            workOrder["msdyn_taxcode"] = new EntityReference("msdyn_taxcode", salestaxcodeId);
                        workOrder["msdyn_taxable"] = true;
                    }
                    else
                    {
                        tracingService.Trace($"The account does not have a tax code.");
                    }
                        tracingService.Trace($"Set billing and service account: {accountId}");
                    workOrder["msdyn_taxable"] = false;

                }

                if (asset.Contains("msdyn_functionallocation"))
                {
                    workOrder["msdyn_functionallocation"] = new EntityReference("msdyn_functionallocation", asset.GetAttributeValue<EntityReference>("msdyn_functionallocation").Id);
                }

                workOrder["msdyn_pricelist"] = new EntityReference("pricelevel", new Guid("062c06f1-31ab-ef11-b8e8-000d3a995f3a"));

                if (incidentTypeID != Guid.Empty)
                {
                    workOrder["msdyn_primaryincidenttype"] = new EntityReference("msdyn_incidenttype", incidentTypeID);
                    tracingService.Trace($"Set primary incident type: {incidentTypeID}");
                }

                if (agreementId!=Guid.Empty)
                {
                    workOrder["msdyn_agreement"] = new EntityReference("msdyn_agreement", agreementId);
                }

                 workOrder["msdyn_customerasset"] = new EntityReference("msdyn_customerasset", asset.Id);
                //workOrder["tb_atsasset"] = new EntityReference("msdyn_customerasset", asset.Id);


                //workOrder["tb_assetserviceplanner"] = new EntityReference("tb_assetserviceplanner", servicePlanner.Id);

                //if (assetDetails.Contains("tb_hours"))
                //{
                //    workOrder["tb_genhours"] = assetDetails.GetAttributeValue<decimal?>("tb_hours");
                //}

                if (servicedate!=null)
                {
                    workOrder["msdyn_timefrompromised"] = new DateTime(servicedate.Year, servicedate.Month, servicedate.Day, 8, 0, 0); // 8 AM;
                    workOrder["msdyn_timetopromised"] = new DateTime(servicedate.Year, servicedate.Month, servicedate.Day, 17, 0, 0).AddDays(14); // 5 PM + 14 days
                }             
                if (asset.Contains("tb_kwsize"))
                {
                    workOrder["tb_genkwsize"] = asset.GetAttributeValue<int>("tb_kwsize");
                }

                if (asset.Contains("tb_model"))
                {
                    workOrder["tb_genmodel"] = asset.GetAttributeValue<string>("tb_model");
                }

                if (asset.Contains("tb_serialnumber"))
                {
                    workOrder["tb_genserialnumber"] = asset.GetAttributeValue<string>("tb_serialnumber");
                }

                tracingService.Trace("Creating work order record...");
                Guid workOrderId = service.Create(workOrder);
                tracingService.Trace($"Work order created successfully with ID: {workOrderId}");
            }
            catch (Exception ex)
            {
                tracingService.Trace($"Error creating work order: {ex.Message}");
                throw new InvalidPluginExecutionException($"Error creating work order: {ex.Message}");
            }
        }

        public Guid GetIncidentTypeIdFromAsset(Guid assetId, int serviceType, IOrganizationService service, ITracingService tracingService)
        {
            try
            {
                var fetchData = new
                {
                    tb_servicetype = serviceType,
                    msdyn_customerassetid = assetId
                };

                var fetchXml = new FetchExpression($@"<fetch>
                    <entity name='msdyn_incidenttype'>
                        <filter>
                            <condition attribute='tb_servicetype' operator='eq' value='{fetchData.tb_servicetype}' />
                            <link-entity name='tb_incidenttype_customerasset' from='msdyn_incidenttypeid' to='msdyn_incidenttypeid' link-type='any' intersect='true'>
                                <filter>
                                    <condition attribute='msdyn_customerassetid' operator='eq' value='{fetchData.msdyn_customerassetid}' />
                                </filter>
                            </link-entity>
                        </filter>
                    </entity>
                </fetch>");

                tracingService.Trace("Fetching incident type using FetchXML...");
                EntityCollection incidentTypeCollection = service.RetrieveMultiple(fetchXml);

                if (incidentTypeCollection.Entities.Count > 0)
                {
                    Guid incidentTypeId = incidentTypeCollection.Entities[0].Id;
                    tracingService.Trace($"Incident type ID found: {incidentTypeId}");
                    return incidentTypeId;
                }
                else
                {
                    tracingService.Trace("No incident type found.");
                    return Guid.Empty;
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"Error retrieving incident type: {ex.Message}");
                throw new InvalidPluginExecutionException($"Error retrieving incident type: {ex.Message}");
            }
        }

        private Guid GetActiveAgreement(Entity asset, IOrganizationService service, ITracingService tracingService)
        {

            try
            {
                tracingService.Trace("get current active Agreement/ if multipe agreements available get the agreement without any service planner records");
                var query_msdyn_systemstatus = 690970001;
                var query_tb_msdyn_agreement_msdyn_customerasset_msdyn_customerassetid = asset.Id;

                var query = new QueryExpression("msdyn_agreement");
                query.TopCount = 50; query.ColumnSet.AllColumns = true;
                query.Criteria.AddCondition("msdyn_systemstatus", ConditionOperator.Equal, query_msdyn_systemstatus);
                var query_tb_msdyn_agreement_msdyn_customerasset = query.AddLink(
                    "tb_msdyn_agreement_msdyn_customerasset",
                    "msdyn_agreementid",
                    "msdyn_agreementid");

                query_tb_msdyn_agreement_msdyn_customerasset.LinkCriteria.AddCondition("msdyn_customerassetid", ConditionOperator.Equal, query_tb_msdyn_agreement_msdyn_customerasset_msdyn_customerassetid);
                var query_msdyn_workorder = query.AddLink("msdyn_workorder", "msdyn_agreementid", "msdyn_agreement", JoinOperator.LeftOuter);

                query_msdyn_workorder.LinkCriteria.AddCondition("msdyn_agreement", ConditionOperator.Null);

                // Retrieve the records 
                EntityCollection activeAgreements = service.RetrieveMultiple(query);


                if (activeAgreements.Entities.Count > 1)
                {
                    tracingService.Trace("" + activeAgreements.Entities.Count + "");
                    throw new InvalidPluginExecutionException($" Asset has multiple active agreements");
                }
                else if (activeAgreements.Entities.Count > 0)
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
    }
}
