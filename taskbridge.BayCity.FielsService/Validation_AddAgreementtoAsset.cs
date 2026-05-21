//<summary>
//An asset can only have one active or renewal agreement at a time. It can have quote or estimated type agreements.
//This plug-in should run synchronously on associate event to show an error for the user.
//</summary>
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FielsService
{
    public class Validation_AddAgreementtoAsset : IPlugin
    {

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            try
            {
                if (context.MessageName == "Associate")
                {
                    string relationshipName = ((Relationship)context.InputParameters["Relationship"]).SchemaName;

                    if (relationshipName == "tb_msdyn_agreement_msdyn_customerasset_msdyn_customerasset")
                    {
                        ValidateAgreementstatus(service, tracingService, context);
                    }
                }               

            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in Execute method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in the Validation_AddAgreementtoAsset plugin: {ex.Message}", ex);
            }
            
        }

        private void ValidateAgreementstatus(IOrganizationService service, ITracingService tracingService, IPluginExecutionContext context)
        {
          
             
                    EntityReference assetEntity = (EntityReference)context.InputParameters["Target"];
                    EntityReferenceCollection relatedEntities = (EntityReferenceCollection)context.InputParameters["RelatedEntities"];

                   
                        tracingService.Trace("get current active Agreement");

                        var query_msdyn_systemstatus = 690970001; //active agreement
                        var query_tb_msdyn_agreement_msdyn_customerasset_msdyn_customerassetid = assetEntity.Id;

                        var query = new QueryExpression("msdyn_agreement");
                        query.ColumnSet.AddColumns("msdyn_systemstatus", "tb_agreementstatus", "msdyn_agreementid", "msdyn_name");
                        query.Criteria.AddCondition("msdyn_systemstatus", ConditionOperator.Equal, query_msdyn_systemstatus);
                        var query_tb_msdyn_agreement_msdyn_customerasset = query.AddLink(
                            "tb_msdyn_agreement_msdyn_customerasset",
                            "msdyn_agreementid",
                            "msdyn_agreementid");
                        query_tb_msdyn_agreement_msdyn_customerasset.Columns.AddColumns(
                            "msdyn_agreementid",
                            "msdyn_customerassetid",
                            "tb_msdyn_agreement_msdyn_customerassetid");
                        query_tb_msdyn_agreement_msdyn_customerasset.LinkCriteria.AddCondition("msdyn_customerassetid", ConditionOperator.Equal, query_tb_msdyn_agreement_msdyn_customerasset_msdyn_customerassetid);

                        EntityCollection activeAgreements = service.RetrieveMultiple(query);

                        if (activeAgreements.Entities.Count > 1)
                        {
                            throw new InvalidPluginExecutionException($"Asset already has an active agreement: An asset can only have one active agreement at a time.");
                        }
       
            }
      
    }
}
