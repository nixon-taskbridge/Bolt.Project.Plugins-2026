using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FielsService
{
         public class AgreementFinancialsCalculation : IPlugin
        {
            private IOrganizationService service;
            private ITracingService tracingService;
            private Guid relatedAgreementGuid;

            private const string InvoiceEntityName = "bolt_invoicing";
            private const string AgreementEntityName = "msdyn_agreement";
            private const string WorkOrderEntityName = "msdyn_workorder";
            private const string PreImageName = "Image";
            private const int ActiveStateCode = 0;

            public void Execute(IServiceProvider serviceProvider)
            {
                // Obtain the tracing service for logging
                tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
                IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                service = serviceFactory.CreateOrganizationService(context.UserId);

                try
                {
                    // Check if the operation is Create or Update
                    if (context.MessageName == "Create" || context.MessageName == "Update")
                    {
                        if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity e )
                        {
                        Entity entity = service.Retrieve(e.LogicalName, e.Id, new ColumnSet(true));
                            // Process either Invoice or Work Order entities
                            if (entity.LogicalName == InvoiceEntityName)
                            {
                                ProcessEntity(context, entity, "tb_relatedagreement");
                            }
                            else if (entity.LogicalName == WorkOrderEntityName)
                            {
                                ProcessEntity(context, entity, "msdyn_agreement");
                            }
                        }
                    }
                    // Check if the operation is Delete
                    else if (context.MessageName == "Delete")
                    {
                        if (context.PreEntityImages.Contains(PreImageName))
                        {
                            var deletedEntity = (Entity)context.PreEntityImages[PreImageName];
                            if (deletedEntity.LogicalName == InvoiceEntityName)
                            {
                                ProcessEntity(context, deletedEntity, "tb_relatedagreement", isDelete: true);
                            }
                            else if (deletedEntity.LogicalName == WorkOrderEntityName)
                            {
                                ProcessEntity(context, deletedEntity, "msdyn_agreement", isDelete: true);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Trace the exception and rethrow it
                    tracingService.Trace("Exception in AgreementInvoice Plugin: {0}", ex.ToString());
                    throw; // Rethrow the exception to propagate it
                }
            }

            private void ProcessEntity(IPluginExecutionContext context, Entity entity, string fieldName, bool isDelete = false)
            {
                // Check if the entity has a related agreement
                if (entity.Attributes.Contains(fieldName))
                {
                    relatedAgreementGuid = entity.GetAttributeValue<EntityReference>(fieldName).Id;
                tracingService.Trace(fieldName,relatedAgreementGuid);
                    try
                    {
                        // Depending on the entity type, calculate the relevant totals
                        if (entity.LogicalName == InvoiceEntityName)
                        {
                            CalculateAgreementInvoiceAmount(relatedAgreementGuid, isDelete);
                        }
                        else if (entity.LogicalName == WorkOrderEntityName)
                        {
                            CalculateAgreementWorkOrderAmount(relatedAgreementGuid, isDelete);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error if processing fails
                        tracingService.Trace("Error processing entity {0} for agreement {1}: {2}", entity.LogicalName, relatedAgreementGuid, ex.ToString());
                        throw; // Rethrow to ensure the error is not suppressed
                    }
                }
            }

            private void CalculateAgreementInvoiceAmount(Guid agreementID, bool isDelete)
            {
                tracingService.Trace("Calculating invoice amount for agreement ID: {0}", agreementID);
                decimal invoiceTotal = GetTotalInvoiceAmount(agreementID);
                UpdateAgreementTotals(agreementID, invoiceTotal, isInvoice: true);
            }

            private void CalculateAgreementWorkOrderAmount(Guid agreementID, bool isDelete)
            {
                tracingService.Trace("Calculating work order amount for agreement ID: {0}", agreementID);
                decimal workOrderTotal = GetTotalWorkOrderAmount(agreementID);
                UpdateAgreementTotals(agreementID, workOrderTotal, isInvoice: false);
            }

            private decimal GetTotalInvoiceAmount(Guid agreementID)
            {
                // Query to retrieve total billed amount for invoices associated with the agreement
                var query = new QueryExpression(InvoiceEntityName)
                {
                    ColumnSet = new ColumnSet("bolt_contractbilledamount"),
                    Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("statecode", ConditionOperator.Equal, ActiveStateCode),
                        new ConditionExpression("tb_relatedagreement", ConditionOperator.Equal, agreementID)
                    }
                }
                };

                EntityCollection invoices = service.RetrieveMultiple(query);
                decimal total = 0.0m;

                // Sum the contract billed amounts
                foreach (var invoice in invoices.Entities)
                {
                    if (invoice.Attributes.Contains("bolt_contractbilledamount"))
                    {
                        total += ((Money)invoice["bolt_contractbilledamount"]).Value;
                    }
                }

                tracingService.Trace("Total invoice amount: {0}", total);
                return total;
            }

            private decimal GetTotalWorkOrderAmount(Guid agreementID)
            {
                // Query to retrieve total amount for work orders associated with the agreement
                var query = new QueryExpression(WorkOrderEntityName)
                {
                    ColumnSet = new ColumnSet("msdyn_totalamount"),
                    Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("statecode", ConditionOperator.Equal, ActiveStateCode),
                        new ConditionExpression("msdyn_agreement", ConditionOperator.Equal, agreementID)
                    }
                }
                };

                EntityCollection workOrders = service.RetrieveMultiple(query);
                decimal total = 0.0m;

                // Sum the total amounts from work orders
                foreach (var workOrder in workOrders.Entities)
                {
                    if (workOrder.Attributes.Contains("msdyn_totalamount"))
                    {
                        total += ((Money)workOrder["msdyn_totalamount"]).Value;
                    }
                }

                tracingService.Trace("Total work order amount: {0}", total);
                return total;
            }

            private void UpdateAgreementTotals(Guid agreementID, decimal total, bool isInvoice)
            {
                try
                {
                    // Retrieve the existing agreement
                    Entity agreement = service.Retrieve(AgreementEntityName, agreementID, new ColumnSet("tb_totalagreementprice", "tb_totalinvoicedamount", "tb_remainingbalance"));

                    // Update the appropriate total on the agreement
                    if (isInvoice)
                    {
                        agreement["tb_totalinvoicedamount"] = total;
                    }
                    else
                    {
                        agreement["tb_totalwoprice"] = total; // Total for work orders
                    }

                    // Calculate the difference
                      //decimal totalAgreementAmount = agreement.GetAttributeValue<Money>("tb_totalagreementprice")?.Value ?? 0;
                    //decimal totalInvoicedAmount = agreement.GetAttributeValue<Money>("tb_totalinvoicedamount")?.Value ?? 0;
                   // decimal difference = totalAgreementAmount - (isInvoice ? total : totalInvoicedAmount);

                    // Update the remaining balance
                   // agreement["tb_remainingbalance"] = difference; // Assuming this field exists

                    // Update the agreement record in CRM
                    service.Update(agreement);
                    tracingService.Trace("Updated agreement totals for ID: {0}", agreementID);
                }
                catch (Exception ex)
                {
                    // Log and throw if updating the agreement fails
                    tracingService.Trace("Error updating agreement totals for ID {0}: {1}", agreementID, ex.ToString());
                    throw;
                }
            }
        }    
}
