// Microsoft Dynamics CRM namespace(s)
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace BOLT.BayCity.Plug.ins
{
    public class CostSheetTotalPMCost : IPlugin
    {
        /// <summary>
        /// A plugin that adds all the related Planned Maintenance and KD records' 'Total contract Price' and updates cost sheet Total PM Cost field.
        /// new_job(Project)
        /// </summary>
        /// <remarks>
        /// Post Operation execution stage, and Synchronous execution mode.
        /// </remarks>

        IOrganizationService service;
        ITracingService tracingService;

        public void Execute(IServiceProvider serviceProvider)
        {
            //Extract the tracing service for use in debugging sandboxed plug-ins.
            tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the execution context from the service provider.
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
            {
                tracingService.Trace("A1");
                // Obtain the target entity from the input parameters.
                Entity entity = (Entity)context.InputParameters["Target"];

                try
                {
                    tracingService.Trace("A2");
                    IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                    service = serviceFactory.CreateOrganizationService(context.UserId);

                    if ((context.MessageName == "Create" || context.MessageName == "Update"))
                    {
                        Entity ent = service.Retrieve(entity.LogicalName, entity.Id, new ColumnSet(true));
                        if (ent.Attributes.Contains("bolt_costsheet"))
                        {
                            EntityReference costsheetRef = ent.GetAttributeValue<EntityReference>("bolt_costsheet");
                            if (costsheetRef != null)
                            {
                                Guid costsheetid = costsheetRef.Id;

                                if ((entity.LogicalName == "bolt_plannedmaintenanceservice"
                                     || entity.LogicalName == "bolt_kdservicemaintenance"
                                     || entity.LogicalName == "bolt_miscitems")
                                    && costsheetid != Guid.Empty)
                                {
                                    decimal pmTotal = PMAmountcalculation(costsheetid);
                                    decimal kdTotal = KDAmountcalculation(costsheetid);
                                    decimal miscTotal = MiscAmountcalculation(costsheetid);
                                    decimal totalamount = pmTotal + kdTotal + miscTotal;

                                    updateCostsheet(costsheetid, totalamount);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    tracingService.Trace("TotalStartupCostSheet: {0}", ex.ToString());
                    throw;
                }
            }
            else if (context.MessageName.ToUpper() == "DELETE")
            {
                IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                service = serviceFactory.CreateOrganizationService(context.UserId);
                Entity preImage = (Entity)context.PreEntityImages["preImage"];
                if (preImage.Contains("bolt_costsheet"))
                {
                    EntityReference costsheetRef = preImage.GetAttributeValue<EntityReference>("bolt_costsheet");
                    if (costsheetRef != null && costsheetRef.Id != Guid.Empty)
                    {
                        Guid id = costsheetRef.Id;
                        decimal pmTotal = PMAmountcalculation(id);
                        decimal kdTotal = KDAmountcalculation(id);
                        decimal miscTotal = MiscAmountcalculation(id);
                        decimal totalamount = pmTotal + kdTotal + miscTotal;
                        updateCostsheet(id, totalamount);
                    }
                }
            }
        }

        public decimal PMAmountcalculation(Guid id)
        {
            tracingService.Trace("1");
            // Define Condition Values
            var query_statecode = 0;
            var query_bolt_costsheet = id;

            // Instantiate QueryExpression query
            var query = new QueryExpression("bolt_plannedmaintenanceservice");

            // Add columns to query.ColumnSet
            query.ColumnSet.AddColumns("bolt_totalcontractamount");

            // Define filter query.Criteria
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, query_statecode);
            query.Criteria.AddCondition("bolt_costsheet", ConditionOperator.Equal, query_bolt_costsheet);

            EntityCollection PMcollection = service.RetrieveMultiple(query);

            // FIX: always start from 0 regardless of whether any records were found,
            // so an empty result correctly produces 0 instead of retaining a stale value.
            decimal totalPMamount = 0.00m;
            foreach (var e in PMcollection.Entities)
            {
                if (e.Attributes.Contains("bolt_totalcontractamount"))
                    totalPMamount += ((Money)e["bolt_totalcontractamount"]).Value;
            }

            tracingService.Trace("2");
            return totalPMamount;
        }

        public decimal KDAmountcalculation(Guid id)
        {
            tracingService.Trace("3");
            // Define Condition Values
            var query_statuscode = 1;
            var query_bolt_costsheet = id;

            // Instantiate QueryExpression query
            var query = new QueryExpression("bolt_kdservicemaintenance");

            // Add columns to query.ColumnSet
            query.ColumnSet.AddColumns("bolt_totalkdcontractprice");

            // Define filter query.Criteria
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, query_statuscode);
            query.Criteria.AddCondition("bolt_costsheet", ConditionOperator.Equal, query_bolt_costsheet);

            EntityCollection kdcollection = service.RetrieveMultiple(query);

            decimal totalKDamount = 0.00m;
            foreach (var e in kdcollection.Entities)
            {
                if (e.Attributes.Contains("bolt_totalkdcontractprice"))
                    totalKDamount += ((Money)e["bolt_totalkdcontractprice"]).Value;
            }

            tracingService.Trace("4");
            return totalKDamount;
        }

        public decimal MiscAmountcalculation(Guid id)
        {
            tracingService.Trace("4");
            // Define Condition Values
            var query_statuscode = 1;
            var query_bolt_costsheet = id;

            // Instantiate QueryExpression query
            var query = new QueryExpression("bolt_miscitems");

            // Add columns to query.ColumnSet
            query.ColumnSet.AddColumns("bolt_miscprice");

            // Define filter query.Criteria
            query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, query_statuscode);
            query.Criteria.AddCondition("bolt_costsheet", ConditionOperator.Equal, query_bolt_costsheet);

            EntityCollection misccollection = service.RetrieveMultiple(query);

            decimal totalMiscamount = 0.00m;
            foreach (var e in misccollection.Entities)
            {
                if (e.Attributes.Contains("bolt_miscprice"))
                    totalMiscamount += ((Money)e["bolt_miscprice"]).Value;
            }

            tracingService.Trace("5");
            return totalMiscamount;
        }

        public void updateCostsheet(Guid csid, decimal totalamount)
        {
            tracingService.Trace("6");
            Entity e = new Entity();
            e.LogicalName = "bolt_costsheet";
            e.Id = csid;
            e["bolt_totalpmcost"] = new Money(totalamount);
            service.Update(e);

            tracingService.Trace("final");
        }
    }
}
