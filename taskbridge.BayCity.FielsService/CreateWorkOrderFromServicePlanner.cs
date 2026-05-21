using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FielsService
{ /// <summary>
  /// This plugin creates a Work Order from a Service Planner or Asset form.
  /// It retrieves related assets and incident types and populates the Work Order with relevant data.
  /// </summary>
    public class CreateWorkOrderFromServicePlanner : IPlugin
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

                    if (target.LogicalName == "tb_assetserviceplanner")
                    {
                        tracingService.Trace("Starting to retrieve service planner record...");
                        Entity servicePlanner = service.Retrieve("tb_assetserviceplanner", target.Id, new ColumnSet("tb_createworkorder", "tb_relatedasset", "tb_servicedate", "tb_servicetype"));

                        if (servicePlanner.Attributes.Contains("tb_relatedasset") &&
                            servicePlanner.Attributes.Contains("tb_servicedate") &&
                            servicePlanner.Attributes.Contains("tb_servicetype")&&
                             servicePlanner.Attributes.Contains("tb_createworkorder")&& 
                             servicePlanner.GetAttributeValue<bool>("tb_createworkorder") is true)
                        {
                            tracingService.Trace("Creating work order...");
                            
                            if (!HasWorkOrders(servicePlanner.Id,service, tracingService))
                            {
                                CreateWorkOrder(servicePlanner,service, tracingService);
                            }
                            else
                            {
                                tracingService.Trace("Work order already exists for this service planner.");
                            }
                        }
                        else
                        {
                            tracingService.Trace("Service planner is missing required fields.");
                        }
                    }
                    //If user perform "Create WorkOrder" from Asset Form
                    if(target.LogicalName == "msdyn_customerasset")
                    {
                        tracingService.Trace("Create work order is marked as Yes on the Asset Form. Now retrieving service planner records.");
                        Entity assetEntity = service.Retrieve("msdyn_customerasset", target.Id, new ColumnSet("tb_createworkorders"));
                        
                        if(assetEntity.Attributes.Contains("tb_createworkorders")&& assetEntity.GetAttributeValue<bool>("tb_createworkorders") is true)
                        {
                            GetAssociatedServicePlannerRecords(service, tracingService, assetEntity.Id);
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"CreateWorkOrderFromServicePlanner Plugin Error: {ex.Message}");
                throw new InvalidPluginExecutionException("An error occurred in the CreateWorkOrderFromServicePlanner plug-in.", ex);
            }
        }

        public void GetAssociatedServicePlannerRecords(IOrganizationService service, ITracingService tracingService, Guid assetId)
        {
            var query_statecode = 0;
            var query_tb_relatedasset = assetId;

            var query = new QueryExpression("tb_assetserviceplanner");
            query.ColumnSet.AllColumns = true;
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, query_statecode);
            query.Criteria.AddCondition("tb_relatedasset", ConditionOperator.Equal, query_tb_relatedasset);
            var query_msdyn_workorder = query.AddLink(
                "msdyn_workorder",
                "tb_assetserviceplannerid",
                "tb_assetserviceplanner",
                JoinOperator.LeftOuter);

            query_msdyn_workorder.LinkCriteria.AddCondition("tb_assetserviceplanner", ConditionOperator.Null);

            EntityCollection spRecords = service.RetrieveMultiple(query);

            if(spRecords.Entities.Count>0)
            {
                foreach(Entity spRecord in spRecords.Entities)
                {
                    if (!HasWorkOrders(spRecord.Id,service, tracingService))
                    {
                        CreateWorkOrder(spRecord, service, tracingService);
                    }
                }
            }

        }
        public void CreateWorkOrder(Entity servicePlanner,IOrganizationService service, ITracingService tracingService)
        {
            try
            {
                int serviceType = servicePlanner.GetAttributeValue<OptionSetValue>("tb_servicetype").Value;
                Guid assetID = servicePlanner.GetAttributeValue<EntityReference>("tb_relatedasset").Id;

                tracingService.Trace($"Fetching asset details for Asset ID: {assetID}");
                Entity assetDetails = service.Retrieve("msdyn_customerasset", assetID, new ColumnSet(true));

                var incidentTypeID = GetIncidentTypeIdFromAsset(assetID, serviceType,service, tracingService);

                Entity workOrder = new Entity("msdyn_workorder");

                workOrder["msdyn_systemstatus"] = new OptionSetValue(690970000);
                workOrder["msdyn_taxable"] = true;

                if (serviceType == 126700000)
                {
                    workOrder["msdyn_workordertype"] = new EntityReference("msdyn_workordertype", new Guid("5b1f41ca-8140-ef11-8409-7c1e5219aec0"));
                    workOrder["msdyn_substatus"] = new EntityReference("msdyn_workordersubstatus", new Guid("816f9f48-ab7a-ef11-a671-6045bddc8882"));

                }
                else if (serviceType == 126700001)
                {
                    workOrder["msdyn_workordertype"] = new EntityReference("msdyn_workordertype", new Guid("6ddc48c4-8140-ef11-8409-7c1e5219aec0"));
                    workOrder["msdyn_substatus"] = new EntityReference("msdyn_workordersubstatus", new Guid("c6ac30a6-ec49-ef11-a317-0022482af524"));
                }

                if (assetDetails.Contains("msdyn_account"))
                {
                    Guid accountId = assetDetails.GetAttributeValue<EntityReference>("msdyn_account").Id;
                    workOrder["msdyn_billingaccount"] = new EntityReference("account", accountId);
                    workOrder["msdyn_serviceaccount"] = new EntityReference("account", accountId);
                    tracingService.Trace($"Set billing and service account: {accountId}");
                }

                if (assetDetails.Contains("msdyn_functionallocation"))
                {
                    workOrder["msdyn_functionallocation"] = new EntityReference("msdyn_functionallocation", assetDetails.GetAttributeValue<EntityReference>("msdyn_functionallocation").Id);
                }

                workOrder["msdyn_pricelist"] = new EntityReference("pricelevel", new Guid("3e9d8aa1-fb24-ef11-840a-0022482d598d"));

                if ( incidentTypeID != Guid.Empty)
                {
                    workOrder["msdyn_primaryincidenttype"] = new EntityReference("msdyn_incidenttype", incidentTypeID);
                    tracingService.Trace($"Set primary incident type: {incidentTypeID}");
                }             

                if (servicePlanner.Contains("tb_agreement"))
                {
                    workOrder["msdyn_agreement"] = new EntityReference("msdyn_agreement", assetDetails.GetAttributeValue<EntityReference>("tb_agreement").Id);
                }

                workOrder["tb_assetserviceplanner"] = new EntityReference("tb_assetserviceplanner", servicePlanner.Id);

                //if (assetDetails.Contains("tb_hours"))
                //{
                //    workOrder["tb_genhours"] = assetDetails.GetAttributeValue<decimal?>("tb_hours");
                //}

                if (assetDetails.Contains("tb_kwsize"))
                {
                    workOrder["tb_genkwsize"] = assetDetails.GetAttributeValue<int>("tb_kwsize");
                }

                if (assetDetails.Contains("tb_model"))
                {
                    workOrder["tb_genmodel"] = assetDetails.GetAttributeValue<string>("tb_model");
                }

                if (assetDetails.Contains("tb_serialnumber"))
                {
                    workOrder["tb_genserialnumber"] = assetDetails.GetAttributeValue<string>("tb_serialnumber");
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

         /// <summary>
        /// Checks if a work order already exists for the given service planner.
        /// </summary>


          public bool HasWorkOrders(Guid servicePlannerId,IOrganizationService service, ITracingService tracingService)
        {
            try
            {
                tracingService.Trace($"Checking for existing work orders for service planner ID: {servicePlannerId}");

                // Query to check if any work orders exist linked to the service planner
                QueryExpression query = new QueryExpression("msdyn_workorder")
                {
                    ColumnSet = new ColumnSet("msdyn_workorderid")
                };
                query.Criteria.AddCondition("tb_assetserviceplanner", ConditionOperator.Equal, servicePlannerId);

                // Execute the query
                EntityCollection workOrders = service.RetrieveMultiple(query);

                // Return true if any work orders are found, otherwise false
                bool hasWorkOrders = workOrders.Entities.Count > 0;
                tracingService.Trace($"Existing work orders found: {hasWorkOrders}");
                return hasWorkOrders;
            }
            catch (Exception ex)
            {
                tracingService.Trace($"Error checking for work orders: {ex.Message}");
                throw new InvalidPluginExecutionException($"Error checking for work orders: {ex.Message}");
            }
         }
    }
}
