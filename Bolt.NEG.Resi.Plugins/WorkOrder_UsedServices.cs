using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Bolt.NEG.Resi.Plugins
{
    public class WorkOrderServiceRate : IPlugin
    {
        IOrganizationService service;
        ITracingService tracingService;
        Guid relatedWO_guid;
        public void Execute(IServiceProvider serviceProvider)
        {

            //Extract the tracing service for use in debugging sandboxed plug-ins.
            tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the execution context from the service provider.
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            service = serviceFactory.CreateOrganizationService(context.UserId);
            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
            {
                // Obtain the target entity from the input parmameters.
                Entity entity = (Entity)context.InputParameters["Target"];
                if (entity.LogicalName == "msdyn_workorderservice")
                {
                    try
                    {
                        tracingService.Trace("Inside entity");
                        if (context.PostEntityImages.Contains("Image")) //update
                        {

                            Entity postImageWOS = (Entity)context.PostEntityImages["Image"]; //msdyn_linestatus, msdyn_description,msdyn_estimateunitamount,

                            if (!postImageWOS.Attributes.Contains("msdyn_workorder") && !postImageWOS.Attributes.Contains("msdyn_linestatus") && !postImageWOS.Attributes.Contains("msdyn_totalamount"))
                                return;

                            var productAmount = postImageWOS.GetAttributeValue<Money>("msdyn_totalamount");  //totalamount                            
                            relatedWO_guid = (postImageWOS.GetAttributeValue<EntityReference>("msdyn_workorder")).Id;

                            if (productAmount != null) //used and true
                            {

                                Calculate_TotalUsedServicesTotal(relatedWO_guid);
                            }


                        }
                        if (context.PreEntityImages.Contains("Image")) //update
                        {
                            tracingService.Trace("Inside Update");
                            Entity preImageWOS = (Entity)context.PreEntityImages["Image"];

                            if (!preImageWOS.Attributes.Contains("msdyn_workorder") && !preImageWOS.Attributes.Contains("msdyn_linestatus") && !preImageWOS.Attributes.Contains("msdyn_totalamount"))
                                return;

                            var productAmount = preImageWOS.GetAttributeValue<Money>("msdyn_totalamount");  //totalamount                            
                            relatedWO_guid = (preImageWOS.GetAttributeValue<EntityReference>("msdyn_workorder")).Id;

                            if (productAmount != null) //used and true
                            {

                                Calculate_TotalUsedServicesTotal(relatedWO_guid);
                            }


                        }
                    }
                    catch (Exception ex)
                    {
                        tracingService.Trace("WorkOrder_UsedServices Plugin", ex.ToString());
                        throw;
                    }
                }
            }
            else if (context.MessageName == "Delete")
            {
                if (context.PreEntityImages.Contains("Image"))
                {
                    tracingService.Trace("Inside Delete");
                    Entity preImageWOS = (Entity)context.PreEntityImages["Image"];
                    if (!preImageWOS.Attributes.Contains("msdyn_workorder") && !preImageWOS.Attributes.Contains("msdyn_linestatus") && !preImageWOS.Attributes.Contains("msdyn_totalamount"))
                        return;
                    var linestatus = preImageWOS.GetAttributeValue<OptionSetValue>("msdyn_linestatus"); //Line Status == Used                   
                    var productAmount = preImageWOS.GetAttributeValue<Money>("msdyn_totalamount");  // total amount               
                    relatedWO_guid = (preImageWOS.GetAttributeValue<EntityReference>("msdyn_workorder")).Id;

                    if (linestatus.Value == 690970001 && productAmount != null)
                    {

                        Calculate_TotalUsedServicesTotal(relatedWO_guid);
                    }
                }
            }
        }
        private void Calculate_TotalUsedServicesTotal(Guid WorderID) //used
        {
            // Define Condition Values
            var query_statuscode = 1; //active                                           
            var query_msdyn_workorder = WorderID;

            // Instantiate QueryExpression query
            var query = new QueryExpression("msdyn_workorderservice");

            // Add columns to query.ColumnSet //"bolt_upsoldproduct"
            query.ColumnSet.AddColumns("msdyn_totalamount", "msdyn_workorder", "msdyn_linestatus", "statuscode");

            // Define filter query.Criteria
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, query_statuscode);
            query.Criteria.AddCondition("msdyn_workorder", ConditionOperator.Equal, query_msdyn_workorder);

            EntityCollection wops = service.RetrieveMultiple(query);


            decimal costtotal_used = 0.0m;
            if (wops.Entities.Count != 0)
            {
                for (int i = 0; i < wops.Entities.Count; i++)
                {

                    if (wops.Entities[i].Attributes.Contains("msdyn_totalamount") && (wops.Entities[i].GetAttributeValue<OptionSetValue>("msdyn_linestatus")).Value == 690970001)
                    {
                        costtotal_used += ((Money)wops.Entities[i]["msdyn_totalamount"]).Value;
                    }
                }

            }


            Entity wo = new Entity("msdyn_workorder");
            wo.Id = relatedWO_guid;
            wo["bolt_usedservicestotal"] = costtotal_used;
            service.Update(wo);

        }
    }
}
