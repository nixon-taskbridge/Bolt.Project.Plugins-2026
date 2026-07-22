using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;


namespace Taskbridge.BayCity.FielsService
{
    public class AddAssetstoCopiedAgreement : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            try
            {
                // Ensure the plugin is executed on Create or Update
                if ((context.MessageName != "Create" && context.MessageName != "Update") || !context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
                {
                    return;
                }

                // Retrieve the Agreement Quote record
                Entity agreement = (Entity)context.InputParameters["Target"];

                // Only proceed if the Agreement lookup is set
                if (agreement.Contains("msdyn_originatingagreement") && agreement["msdyn_originatingagreement"] != null)
                {
                    EntityReference originatingAgreementReference = (EntityReference)agreement["msdyn_originatingagreement"];
                    Guid originatingAgreementID = originatingAgreementReference.Id;

                    tracingService.Trace("Originating Agreement ID: {0}", originatingAgreementID);

                    // If it's an Update operation, remove previous asset associations
                    if (context.MessageName == "Update")
                    {
                        if (context.PreEntityImages.Contains("PreImage") && context.PreEntityImages["PreImage"] is Entity preImage)
                        {
                            if (preImage.Contains("msdyn_originatingagreement"))
                            {
                                EntityReference oldAgreementReference = (EntityReference)preImage["msdyn_originatingagreement"];
                                Guid oldAgreementId = oldAgreementReference.Id;

                                // Only remove associations if the Agreement value has changed
                                if (oldAgreementId != originatingAgreementID)
                                {
                                    tracingService.Trace("Old Agreement ID: {0}", oldAgreementId);
                                    RemoveAssetAssociations(service, agreement.Id, tracingService);
                                }
                            }
                        }
                    }

                    // Retrieve and associate assets from the new agreement
                    AssociateAssets(service, originatingAgreementID, agreement.Id, tracingService);
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in Execute method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in the AssociateAssetsToAgreementQuote plugin: {ex.Message}", ex);
            }
        }

        // Method to remove asset associations from the agreement quote
        private void RemoveAssetAssociations(IOrganizationService service, Guid agreementQuoteId, ITracingService tracingService)
        {
            try
            {
                tracingService.Trace("Removing existing asset associations");

                // Query to retrieve existing asset associations to this agreement quote
                QueryExpression query = new QueryExpression("tb_msdyn_customerasset_tb_agreementquote");
                query.ColumnSet = new ColumnSet(true);
                query.Criteria.AddCondition("tb_agreementquoteid", ConditionOperator.Equal, agreementQuoteId);

                EntityCollection relatedAssociations = service.RetrieveMultiple(query);

                tracingService.Trace("Number of related assets found: {0}", relatedAssociations.Entities.Count);

                tracingService.Trace(relatedAssociations.Entities[0].GetAttributeValue<Guid>("msdyn_customerassetid").ToString());
                // Disassociate each asset from the agreement quote
                foreach (Entity association in relatedAssociations.Entities)
                {
                    // Create the relationship name for the many-to-many relationship
                    Relationship relationship = new Relationship("tb_msdyn_customerasset_tb_AgreementQuote_tb_AgreementQuote");

                    // Create an EntityReferenceCollection to store the asset references to disassociate
                    EntityReferenceCollection relatedAssetsCollection = new EntityReferenceCollection {
                new EntityReference("msdyn_customerasset", association.GetAttributeValue<Guid>("msdyn_customerassetid"))
                };

                    // Create the DisassociateRequest
                    DisassociateRequest disassociateRequest = new DisassociateRequest
                    {
                        Target = new EntityReference("tb_agreementquote", agreementQuoteId),
                        RelatedEntities = relatedAssetsCollection,
                        Relationship = relationship
                    };

                    // Execute the DisassociateRequest
                    service.Execute(disassociateRequest);
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in RemoveAssetAssociations method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred while removing asset associations: {ex.Message}", ex);
            }
        }

        // Method to associate assets from the new agreement to the agreement quote
        private void AssociateAssets(IOrganizationService service, Guid originatingAgreementID, Guid newAgreementID, ITracingService tracingService)
        {
            try
            {
                tracingService.Trace("Associating assets from new agreement");

                // Query to retrieve all assets associated with the originating agreement
                var query_tb_msdyn_agreement_msdyn_customerasset_msdyn_agreementid = originatingAgreementID;

                var query = new QueryExpression("msdyn_customerasset");
                query.ColumnSet.AllColumns = true;
                var query_tb_msdyn_agreement_msdyn_customerasset = query.AddLink(
                    "tb_msdyn_agreement_msdyn_customerasset",
                    "msdyn_customerassetid",
                    "msdyn_customerassetid");

                query_tb_msdyn_agreement_msdyn_customerasset.LinkCriteria.AddCondition("msdyn_agreementid", ConditionOperator.Equal, query_tb_msdyn_agreement_msdyn_customerasset_msdyn_agreementid);

                EntityCollection relatedAssets = service.RetrieveMultiple(query);

                tracingService.Trace("Number of assets found: {0}", relatedAssets.Entities.Count);

                // Create AssociateRequest for each asset and link it to the Agreement
                foreach (Entity asset in relatedAssets.Entities)
                {
                    // Create the relationship name for the many-to-many relationship
                    Relationship relationship = new Relationship("tb_msdyn_agreement_msdyn_customerasset_msdyn_customerasset");

                    // Create an EntityReferenceCollection to store the assets to associate
                    EntityReferenceCollection relatedAssetsCollection = new EntityReferenceCollection
                        {
                            new EntityReference("msdyn_customerasset", asset.Id)
                        };

                    // Create the AssociateRequest
                    AssociateRequest associateRequest = new AssociateRequest
                    {
                        Target = new EntityReference("msdyn_agreement", newAgreementID),
                        RelatedEntities = relatedAssetsCollection,
                        Relationship = relationship
                    };

                    // Execute the AssociateRequest
                    service.Execute(associateRequest);
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in AssociateAssets method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred while associating assets: {ex.Message}", ex);
            }
        }
    }
}
