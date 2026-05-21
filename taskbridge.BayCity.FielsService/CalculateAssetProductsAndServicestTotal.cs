using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// Microsoft Dynamics CRM namespace(s)
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace taskbridge.BayCity.FielsService
{
   public class CalculateAssetProductsAndServicestTotal : IPlugin
    {
       private IOrganizationService _service;
        private ITracingService _tracingService;

        public void Execute(IServiceProvider serviceProvider)
        {
            // Initialize tracing _service to log information for debugging
            _tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the plugin execution context to access data passed to the plugin
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            // Create an organization _service to interact with Dynamics 365 data
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            _service = serviceFactory.CreateOrganizationService(context.UserId);

            if (context.MessageName == "Associate" || context.MessageName == "Disassociate")
            {
                HandleAssociateorDisassociateAction(context);
            }
            else if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity entity)
            {
                HandleCreateorUpdateAction(entity, context);
            }            

        }

        private void HandleAssociateorDisassociateAction(IPluginExecutionContext context)
        {
            string relationshipName = ((Relationship)context.InputParameters["Relationship"]).SchemaName;

            if (relationshipName == "tb_msdyn_incidenttype_msdyn_customerasset_msdyn_customerasset")
            {
                EntityReference targetEntity = (EntityReference)context.InputParameters["Target"];
                EntityReferenceCollection relatedEntities = (EntityReferenceCollection)context.InputParameters["RelatedEntities"];

                if (relatedEntities.Count > 0)
                {
                    RecalculateAndUpdateAssetTotals(targetEntity.Id);
                }
            }          

          }

        private void HandleCreateorUpdateAction(Entity entity, IPluginExecutionContext context)
        {

            if (entity.LogicalName == "msdyn_customerasset")
            {
                try
                {
                   // Entity image = context.PostEntityImages.Contains("Image")? context.PostEntityImages["Image"]: context.PreEntityImages.Contains("Image")? context.PreEntityImages["Image"]: null;

                    //if (image != null)
//{
                        RecalculateAndUpdateAssetTotals(entity.Id);
                   // }
                }
                catch (Exception ex)
                {
                    _tracingService.Trace("Asset_PriceCal Plugin Error: {0}", ex.ToString());
                    throw; // Ensure exception is visible to the user/system
                }
            }
           
           
        }

        private void RecalculateAndUpdateAssetTotals(Guid customerAssetId)
        {
            // Retrieve all related incidents for the target customer asset
            var incidentTypes = RetrieveAssociatedIncidentTypes(customerAssetId);

            decimal major_productsPrice = 0;
            decimal major_servicesPrice = 0;

            decimal major_productsCost = 0;
            decimal major_servicesCost = 0;

            decimal minor_productsPrice = 0;
            decimal minor_servicesPrice = 0;

            decimal minor_productsCost = 0;
            decimal minor_servicesCost = 0;

            // Recalculate the total price based on all associated incident types
            foreach (var incidentType in incidentTypes.Entities)
            {
                if (incidentType.Contains("tb_servicetype"))
                {
                    var incidentTypeOption = (OptionSetValue)incidentType["tb_servicetype"];

                    switch (incidentTypeOption.Value)
                    {
                        case 126700000: // Major
                            major_productsPrice += incidentType.Attributes.Contains("tb_productstotal") ? ((Money)incidentType["tb_productstotal"]).Value : 0;

                            major_servicesPrice += incidentType.Attributes.Contains("tb_servicestotal") ? ((Money)incidentType["tb_servicestotal"]).Value : 0;

                            major_productsCost += incidentType.Attributes.Contains("tb_productstotalcost") ? ((Money)incidentType["tb_productstotalcost"]).Value : 0;

                            major_servicesCost += incidentType.Attributes.Contains("tb_servicestotalcost") ? ((Money)incidentType["tb_servicestotalcost"]).Value : 0;
                            break;
                        case 126700001: // Minor
                            minor_productsPrice += incidentType.Attributes.Contains("tb_productstotal") ? ((Money)incidentType["tb_productstotal"]).Value : 0;

                            minor_servicesPrice += incidentType.Attributes.Contains("tb_servicestotal") ? ((Money)incidentType["tb_servicestotal"]).Value : 0;

                            minor_productsCost += incidentType.Attributes.Contains("tb_productstotalcost") ? ((Money)incidentType["tb_productstotalcost"]).Value : 0;

                            minor_servicesCost += incidentType.Attributes.Contains("tb_servicestotalcost") ? ((Money)incidentType["tb_servicestotalcost"]).Value : 0;
                            break;
                    }
                    //productsPrice += incidentType.Attributes.Contains("tb_productstotal") ? ((Money)incidentType["tb_productstotal"]).Value : 0;

                    //servicesPrice += incidentType.Attributes.Contains("tb_servicestotal") ? ((Money)incidentType["tb_servicestotal"]).Value : 0;

                    //productsCost += incidentType.Attributes.Contains("tb_productstotalcost") ? ((Money)incidentType["tb_productstotalcost"]).Value : 0;

                    //servicesCost += incidentType.Attributes.Contains("tb_servicestotalcost") ? ((Money)incidentType["tb_servicestotalcost"]).Value : 0;

                }

                //if (incidentType.Contains("tb_servicetype"))
                //{
                //    var incidentTypeOption = (OptionSetValue)incidentType["tb_servicetype"];
                //    var estimateTotal = ((Money)incidentType["tb_estimatetotal"]).Value;

                //    switch (incidentTypeOption.Value)
                //    {
                //        case 126700000: // Major
                //            majorPrice += estimateTotal;
                //            break;
                //        case 126700001: // Minor
                //            minorPrice += estimateTotal;
                //            break;
                //    }
                //}
            }

            UpdateAssetTotals(customerAssetId, major_productsPrice, major_servicesPrice, major_productsCost, major_servicesCost, minor_productsPrice, minor_servicesPrice, minor_productsCost, minor_servicesCost);
        }

        private EntityCollection RetrieveAssociatedIncidentTypes(Guid customerAssetId)
        {
            var query = new QueryExpression("msdyn_incidenttype")
            {
                ColumnSet = new ColumnSet("tb_servicetype", "tb_estimatetotal", "tb_productstotal", "tb_servicestotal", "tb_productstotalcost", "tb_servicestotalcost")
            };

            var link = query.AddLink("tb_incidenttype_customerasset", "msdyn_incidenttypeid", "msdyn_incidenttypeid", JoinOperator.Inner);
            link.LinkCriteria.AddCondition("msdyn_customerassetid", ConditionOperator.Equal, customerAssetId);

            return _service.RetrieveMultiple(query);
        }

        private void UpdateAssetTotals(Guid id, decimal ma_productPrice, decimal ma_servicePrice, decimal ma_productCost, decimal ma_serviceCost, decimal mi_productPrice, decimal mi_servicePrice, decimal mi_productCost, decimal mi_serviceCost)
        {
            int noofMajors = 0;
            int noofMinors = 0;
            Entity asset = _service.Retrieve("msdyn_customerasset", id, new ColumnSet("tb_ofmajorservices", "tb_ofminorservices"));
            if(asset.Attributes.Contains("tb_ofmajorservices"))
             noofMajors = asset.GetAttributeValue<int>("tb_ofmajorservices");
            if (asset.Attributes.Contains("tb_ofminorservices"))
                noofMinors = asset.GetAttributeValue<int>("tb_ofminorservices");

            decimal totalPrice = (ma_productPrice * noofMajors) + (mi_productPrice * noofMinors) + (ma_servicePrice * noofMajors) + (mi_servicePrice * noofMinors);
            decimal totalCost = (ma_productCost * noofMajors) + (mi_productCost * noofMinors)+ (ma_serviceCost * noofMajors) + (mi_serviceCost * noofMinors);

            Entity e = new Entity("msdyn_customerasset");
            e.Id = asset.Id;
            e["tb_permajor"] = ma_productPrice + ma_servicePrice;
            e["tb_perminor"] = mi_productPrice + mi_servicePrice;
            //e["tb_productstotal"] = (ma_productPrice * noofMajors) + (mi_productPrice * noofMinors);
            //e["tb_servicestotal"] = (ma_servicePrice * noofMajors) + (mi_servicePrice * noofMinors);
            //e["tb_productstotalcost"] = (ma_productCost * noofMajors) + (mi_productCost * noofMinors); 
            //e["tb_servicestotalcost"] = (ma_serviceCost * noofMajors) + (mi_serviceCost * noofMinors);
            //e["tb_totalfromincidenttype"] = totalPrice;
            //e["tb_totalcostfromincidenttype"] = totalCost;
              _service.Update(e);
        }               
}
}