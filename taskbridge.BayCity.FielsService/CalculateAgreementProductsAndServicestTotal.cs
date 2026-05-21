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
    public class CalculateAgreementProductsAndServicestTotal : IPlugin
    {
        IOrganizationService service;
        ITracingService tracingService;

        public void Execute(IServiceProvider serviceProvider)
        {
            // Initialize tracing service to log information for debugging
            tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the plugin execution context to access data passed to the plugin
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            // Create an organization service to interact with Dynamics 365 data
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            service = serviceFactory.CreateOrganizationService(context.UserId);

            if (context.MessageName.ToLower() == "delete" && context.InputParameters.Contains("Target") && context.InputParameters["Target"] is EntityReference target)
            {
                DeleteAction(target, context);
            }
            else if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity entity) //create or update action
            {
                CreateorUpdateAction(entity, context);
            }


        }

        private void CreateorUpdateAction(Entity entity, IPluginExecutionContext context)
        {
            if (entity.LogicalName == "msdyn_customerasset")
            {
                try
                {
                    // Check if the pre or post images contain an "Image"
                    if (context.PreEntityImages.Contains("Image") || context.PostEntityImages.Contains("Image"))
                    {
                        Entity Image = context.PostEntityImages.Contains("Image") ? (Entity)context.PostEntityImages["Image"] : (Entity)context.PreEntityImages["Image"];


                        Guid? relatedAgreement_guid = Image.GetAttributeValue<EntityReference>("tb_agreement")?.Id;


                        if (relatedAgreement_guid != null)
                        {
                            Calculate_AgreementProductsandServicePrice(relatedAgreement_guid, context, entity.Id);
                        }

                    }

                }
                catch (Exception ex)
                {
                    // Log any exceptions that occur for debugging purposes
                    tracingService.Trace("Agreement_ProductsandServicesPriceCal: {0}", ex.ToString());
                    throw; // Rethrow the exception to ensure it's visible to the user/system
                }
            }
            else if(entity.LogicalName == "msdyn_agreement")
            {
                try
                {                   
                        Calculate_FinalAmount(entity);

                }
                catch (Exception ex)
                {
                    // Log any exceptions that occur for debugging purposes
                    tracingService.Trace("Error calculating final amount for agreement with ID: {0}. Exception: {1}", entity.Id, ex.ToString());
                    throw; // Rethrow the exception to ensure it's visible to the user/system
                }
            }

        }
        private void DeleteAction(EntityReference target, IPluginExecutionContext context)
        {
            // Retrieve the pre-image to access the entity data before deletion
            if (target.LogicalName == "msdyn_customerasset" && context.PreEntityImages.Contains("PreImage"))
            {
                Entity preImage = context.PreEntityImages["PreImage"];
                Guid relatedAgreementGuid = preImage.GetAttributeValue<EntityReference>("tb_agreement")?.Id ?? Guid.Empty;
                Guid assetId = target.Id;

                // Recalculate agreement totals if there was an associated agreement
                if (relatedAgreementGuid != Guid.Empty)
                {
                    Calculate_AgreementProductsandServicePrice(relatedAgreementGuid, context, assetId);
                }
            }
        }        
        

        private void Calculate_AgreementProductsandServicePrice(Guid? agreementGuid, IPluginExecutionContext context, Guid assetId)
        {
            // Define Condition Values
            var query_statecode = 0;
            var query_tb_agreement = agreementGuid;

            // Instantiate QueryExpression query
            var query = new QueryExpression("msdyn_customerasset");

            // Add columns to query.ColumnSet
            query.ColumnSet.AddColumns("tb_agreement", "tb_productstotal", "tb_productstotalcost","tb_servicestotal", "tb_servicestotalcost", "tb_totalfromincidenttype", "statecode");

            // Define filter query.Criteria
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, query_statecode);
            query.Criteria.AddCondition("tb_agreement", ConditionOperator.Equal, query_tb_agreement);

            if (context.MessageName == "Delete" || context.PreEntityImages.Contains("Image")) //on delete action or when Agreement Lookup field' is cleared on asset form.
                query.Criteria.AddCondition("msdyn_customerassetid", ConditionOperator.NotEqual, assetId);

            EntityCollection assets = service.RetrieveMultiple(query);

            decimal productsTotal = 0.0m;
            decimal servicesTotal = 0.0m;
            decimal productsTotal_Cost = 0.0m;
            decimal servicesTotal_Cost = 0.0m;

            if (assets.Entities.Count != 0)
            {
                for (int i = 0; i < assets.Entities.Count; i++)
                {
                    if (assets.Entities[i].Attributes.Contains("tb_productstotal"))
                    {
                        productsTotal += (((Money)assets.Entities[i]["tb_productstotal"]).Value);
                    }
                    if (assets.Entities[i].Attributes.Contains("tb_productstotalcost"))
                    {
                        productsTotal_Cost += (((Money)assets.Entities[i]["tb_productstotalcost"]).Value);
                    }
                    if (assets.Entities[i].Attributes.Contains("tb_servicestotal"))
                    {
                        servicesTotal += ((Money)assets.Entities[i]["tb_servicestotal"]).Value;
                    }
                    if (assets.Entities[i].Attributes.Contains("tb_servicestotalcost"))
                    {
                        servicesTotal_Cost += ((Money)assets.Entities[i]["tb_servicestotalcost"]).Value;
                    }

                }

            }

            Entity agreement = new Entity("msdyn_agreement");
            agreement.Id = (Guid)agreementGuid;
            agreement["tb_productstotalprice"] = productsTotal;
            agreement["tb_servicetotalprice"] = servicesTotal;
            agreement["tb_productstotalcost"] = productsTotal_Cost;
            agreement["tb_servicetotalcost"] = servicesTotal_Cost;
            agreement["tb_originalagreementprice"] = productsTotal + servicesTotal;
            service.Update(agreement);
        }

        private void Calculate_FinalAmount(Entity entity)
        {
            if (entity.LogicalName == "msdyn_agreement")
            {
                Entity agreement = service.Retrieve("msdyn_agreement", entity.Id, new ColumnSet(true));
                // Retrieve necessary fields with null checks
                decimal originalPrice = agreement.Contains("tb_originalagreementprice") ? agreement.GetAttributeValue<Money>("tb_originalagreementprice").Value : 0m;
                decimal markupPercentage = agreement.Contains("tb_markup") ? agreement.GetAttributeValue<decimal>("tb_markup") : 0m;
                decimal discountPercentage = agreement.Contains("tb_discount") ? agreement.GetAttributeValue<decimal>("tb_discount") : 0m;
                decimal taxPercentage = agreement.Contains("tb_tax") ? agreement.GetAttributeValue<decimal>("tb_tax") : 0m;

                // Calculate markup, discount and tax amounts
                decimal markupAmount = originalPrice * (markupPercentage / 100);
                decimal discountAmount = originalPrice * (discountPercentage / 100);
                decimal priceAfterMarkupDiscount = originalPrice + markupAmount - discountAmount;
                decimal taxAmount = priceAfterMarkupDiscount * (taxPercentage / 100);

                // Calculate Total Agreement Price after markup andd discount
                decimal totalAgreementPrice = priceAfterMarkupDiscount + taxAmount;

                //calculate Labor and Products Profit Margin.
                decimal productsTotalCost = agreement.Contains("tb_productstotalcost") ? agreement.GetAttributeValue<Money>("tb_productstotalcost").Value : 0m;
                decimal productsTotalPrice = agreement.Contains("tb_productstotalprice") ? agreement.GetAttributeValue<Money>("tb_productstotalprice").Value : 0m;
                decimal servicesTotalCost = agreement.Contains("tb_servicetotalcost") ? agreement.GetAttributeValue<Money>("tb_servicetotalcost").Value : 0m;
                decimal servicesTotalPrice = agreement.Contains("tb_servicetotalprice") ? agreement.GetAttributeValue<Money>("tb_servicetotalprice").Value : 0m;

                // Adjust products and services total price with markup and discount
                decimal adjustedProductsTotalPrice = productsTotalPrice * (1 + markupPercentage / 100) * (1 - discountPercentage / 100);
                decimal adjustedServicesTotalPrice = servicesTotalPrice * (1 + markupPercentage / 100) * (1 - discountPercentage / 100);

                // Calculate Profit Margins
                decimal laborProfitMarginPercentage = (adjustedServicesTotalPrice - servicesTotalCost) / adjustedServicesTotalPrice * 100;
                decimal productsProfitMarginPercentage = (adjustedProductsTotalPrice - productsTotalCost) / adjustedProductsTotalPrice * 100;

                decimal laborProfitMargin = (adjustedServicesTotalPrice - servicesTotalCost);
                decimal productsProfitMargin = (adjustedProductsTotalPrice - productsTotalCost);


                Entity e = new Entity("msdyn_agreement");
                e.Id = entity.Id;
                // Set the Total Agreement Price on the entity
                e["tb_totalagreementprice"] = new Money(totalAgreementPrice);
                e["tb_laborprofitmargin"] = laborProfitMargin;
                e["tb_productsprofitmargin"] = productsProfitMargin;
                e["tb_laborprofitmarginpercentage"] = laborProfitMarginPercentage;
                e["tb_productsprofitmarginpercentage"] = productsProfitMarginPercentage;

                service.Update(e);
            }
        }
    }
}
