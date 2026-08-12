using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace taskbridge.BayCity.FieldService
{
    /// <summary>
    /// Pricing for Agreement Booking Product and Agreement Booking Service.
    ///
    /// Product:
    ///   Sell = Qty To Bill × Unit Amount
    ///   Cost = Actual Qty × Unit Cost
    ///
    /// Service:
    ///   # Techs comes from Agreement Booking Incident -> Incident Type.
    ///   Duration To Bill = Duration × # Techs
    ///   Sell = Billable Hours × Unit Amount
    ///   Cost = Technician Hours × Unit Cost
    ///
    /// Register Create/Update as PreOperation, Synchronous.
    /// </summary>
    public class AgreementBookingLinePricing : IPlugin
    {
        // Tables
        private const string AgreementBookingProductTable = "msdyn_agreementbookingproduct";
        private const string AgreementBookingServiceTable = "msdyn_agreementbookingservice";
        private const string AgreementBookingIncidentTable = "msdyn_agreementbookingincident";
        private const string IncidentTypeTable = "msdyn_incidenttype";
        private const string PriceListItemTable = "productpricelevel";

        // Common fields
        private const string PriceListField = "msdyn_pricelist";
        private const string UnitField = "msdyn_unit";
        private const string UnitAmountField = "msdyn_unitamount";
        private const string UnitCostField = "tb_unitcost";
        private const string TotalSellPriceField = "tb_totalsellprice";
        private const string TotalCostField = "tb_totalcost";

        // Product fields
        private const string ProductField = "msdyn_product";
        private const string QuantityField = "msdyn_quantity";
       // private const string QuantityToBillField = "msdyn_qtytobill";

        // Service fields
        private const string ServiceField = "msdyn_service";
        private const string AgreementBookingIncidentField = "msdyn_agreementbookingincident";
        private const string DurationField = "msdyn_duration";
        //private const string DurationToBillField = "msdyn_durationtobill";
        private const string AgreementServiceNumberOfTechsField = "tb_numberoftechs";

        // Incident Type fields
        private const string IncidentTypeField = "msdyn_incidenttype";
        private const string IncidentTypeNumberOfTechsField = "tb_numberoftechs";

        // Price List Item fields
        private const string PriceListAmountField = "amount";
        private const string PriceListCostField = "bolt_cost";
        private const string PriceListItemPriceListField = "pricelevelid";
        private const string PriceListItemProductField = "productid";
        private const string PriceListItemUnitField = "uomid";


        public void Execute(IServiceProvider serviceProvider)
        {
            var context =
                (IPluginExecutionContext)serviceProvider.GetService(
                    typeof(IPluginExecutionContext));

            var tracing =
                (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));

            var serviceFactory =
                (IOrganizationServiceFactory)serviceProvider.GetService(
                    typeof(IOrganizationServiceFactory));

            var service =
                serviceFactory.CreateOrganizationService(context.UserId);

            tracing.Trace(
                "AgreementBookingLinePricing START. Message={0}, Entity={1}, Stage={2}, Depth={3}",
                context.MessageName,
                context.PrimaryEntityName,
                context.Stage,
                context.Depth);

            bool isCreate =
                string.Equals(
                    context.MessageName,
                    "Create",
                    StringComparison.OrdinalIgnoreCase);

            bool isUpdate =
                string.Equals(
                    context.MessageName,
                    "Update",
                    StringComparison.OrdinalIgnoreCase);

            if (!isCreate && !isUpdate)
            {
                tracing.Trace("Not Create/Update. Exiting.");
                return;
            }

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target))
            {
                tracing.Trace("Target missing. Exiting.");
                return;
            }

            // On Update: Target = changed values, PreImage = unchanged/existing values.
            Entity preImage = null;

            if (context.PreEntityImages != null &&
                context.PreEntityImages.Contains("PreImage"))
            {
                preImage = context.PreEntityImages["PreImage"];
            }

            try
            {
                switch (target.LogicalName)
                {
                    case AgreementBookingProductTable:
                        CalculateProductPricing(
                            target,
                            preImage,
                            service,
                            tracing,
                            isCreate);
                        break;

                    case AgreementBookingServiceTable:
                        CalculateServicePricing(
                            target,
                            preImage,
                            service,
                            tracing,
                            isCreate);
                        break;

                    default:
                        tracing.Trace(
                            "Unsupported entity: {0}",
                            target.LogicalName);
                        return;
                }

                tracing.Trace("AgreementBookingLinePricing COMPLETED.");
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracing.Trace(
                    "Unexpected pricing error: {0}",
                    ex);

                throw new InvalidPluginExecutionException(
                    "An unexpected error occurred while calculating Agreement Booking pricing.",
                    ex);
            }
        }


        /// <summary>
        /// Product:
        /// Sell = Quantity To Bill × Unit Amount
        /// Cost = Actual Quantity × Unit Cost
        /// </summary>
        private void CalculateProductPricing(
            Entity target,
            Entity preImage,
            IOrganizationService service,
            ITracingService tracing,
            bool isCreate)
        {
            tracing.Trace("Starting Product pricing.");

            EntityReference priceList =
                GetEntityReference(target, preImage, PriceListField);

            EntityReference product =
                GetEntityReference(target, preImage, ProductField);

            EntityReference unit =
                GetEntityReference(target, preImage, UnitField);

            decimal? sellPrice =
                GetMoneyValue(target, preImage, UnitAmountField);

            decimal? unitCost =
                GetMoneyValue(target, preImage, UnitCostField);

            // Refresh rate only when pricing inputs change or rates are missing.
            bool refreshPrice =
                isCreate ||
                target.Contains(PriceListField) ||
                target.Contains(ProductField) ||
                target.Contains(UnitField) ||
                !sellPrice.HasValue ||
                !unitCost.HasValue;

            if (refreshPrice)
            {
                if (priceList == null || product == null)
                {
                    tracing.Trace(
                        "Product pricing skipped: Price List or Product missing.");
                    return;
                }

                Entity priceListItem =
                    GetPriceListItem(
                        service,
                        priceList.Id,
                        product.Id,
                        unit?.Id,
                        tracing);

                if (priceListItem == null)
                {
                    throw new InvalidPluginExecutionException(
                        "No Price List Item was found for the selected Product and Price List.");
                }

                sellPrice =
                    priceListItem
                        .GetAttributeValue<Money>(PriceListAmountField)
                        ?.Value ?? 0m;

                unitCost =
                    priceListItem
                        .GetAttributeValue<Money>(PriceListCostField)
                        ?.Value ?? 0m;

                target[UnitAmountField] = new Money(sellPrice.Value);
                target[UnitCostField] = new Money(unitCost.Value);

                tracing.Trace(
                    "Product price retrieved. Sell={0}, Cost={1}",
                    sellPrice,
                    unitCost);
            }

            decimal actualQuantity =
                GetDecimal(target, preImage, QuantityField) ?? 0m;

            decimal billableQuantity =
                GetDecimal(target, preImage, QuantityField)
                ?? actualQuantity;

            decimal totalSell =
                Math.Round(
                    billableQuantity * (sellPrice ?? 0m),
                    2);

            decimal totalCost =
                Math.Round(
                    actualQuantity * (unitCost ?? 0m),
                    2);

            target[TotalSellPriceField] = new Money(totalSell);
            target[TotalCostField] = new Money(totalCost);

            tracing.Trace(
                "PRODUCT RESULT: ActualQty={0}, BillQty={1}, Sell={2}, Cost={3}, TotalSell={4}, TotalCost={5}",
                actualQuantity,
                billableQuantity,
                sellPrice,
                unitCost,
                totalSell,
                totalCost);
        }


        /// <summary>
        /// Service:
        /// # Techs = Incident Type # Techs
        /// Duration To Bill = Duration × # Techs
        /// Sell/Cost = Technician Hours × hourly rates
        /// </summary>
        private void CalculateServicePricing(
            Entity target,
            Entity preImage,
            IOrganizationService service,
            ITracingService tracing,
            bool isCreate)
        {
            tracing.Trace("Starting Service pricing.");

            EntityReference priceList =
                GetEntityReference(target, preImage, PriceListField);

            EntityReference serviceProduct =
                GetEntityReference(target, preImage, ServiceField);

            EntityReference unit =
                GetEntityReference(target, preImage, UnitField);


            // -----------------------------
            // 1. NUMBER OF TECHS
            // -----------------------------

            int numberOfTechs = 0;

            if (isCreate)
            {
                // On Create, copy # Techs from Incident Type.
                int? incidentTypeTechs =
                    GetNumberOfTechsFromIncidentType(
                        target,
                        preImage,
                        service,
                        tracing);

                if (incidentTypeTechs.HasValue)
                {
                    numberOfTechs = incidentTypeTechs.Value;
                }
                else
                {
                    numberOfTechs =
                        GetInt(
                            target,
                            preImage,
                            AgreementServiceNumberOfTechsField)
                        ?? 1;
                }

                if (numberOfTechs <= 0)
                {
                    numberOfTechs = 1;
                }

                target[AgreementServiceNumberOfTechsField] =
                    numberOfTechs;

                tracing.Trace(
                    "# Techs assigned on Create: {0}",
                    numberOfTechs);
            }
            else
            {
                // On Update, use the Agreement Booking Service value.
                numberOfTechs =
                    GetInt(
                        target,
                        preImage,
                        AgreementServiceNumberOfTechsField)
                    ?? 0;

                // If Agreement Booking Incident changed, refresh # Techs.
                if (target.Contains(AgreementBookingIncidentField))
                {
                    int? incidentTypeTechs =
                        GetNumberOfTechsFromIncidentType(
                            target,
                            preImage,
                            service,
                            tracing);

                    if (incidentTypeTechs.HasValue)
                    {
                        numberOfTechs = incidentTypeTechs.Value;

                        target[AgreementServiceNumberOfTechsField] =
                            numberOfTechs;
                    }
                }

                // Final fallback.
                if (numberOfTechs <= 0)
                {
                    int? incidentTypeTechs =
                        GetNumberOfTechsFromIncidentType(
                            target,
                            preImage,
                            service,
                            tracing);

                    numberOfTechs =
                        incidentTypeTechs ?? 1;

                    target[AgreementServiceNumberOfTechsField] =
                        numberOfTechs;
                }
            }


            // -----------------------------
            // 2. PRICE LIST RATES
            // -----------------------------

            decimal? sellRate =
                GetMoneyValue(
                    target,
                    preImage,
                    UnitAmountField);

            decimal? costRate =
                GetMoneyValue(
                    target,
                    preImage,
                    UnitCostField);

            bool refreshPrice =
                isCreate ||
                target.Contains(PriceListField) ||
                target.Contains(ServiceField) ||
                target.Contains(UnitField) ||
                !sellRate.HasValue ||
                !costRate.HasValue;

            if (refreshPrice)
            {
                if (priceList == null || serviceProduct == null)
                {
                    tracing.Trace(
                        "Service pricing skipped: Price List or Service missing.");
                    return;
                }

                Entity priceListItem =
                    GetPriceListItem(
                        service,
                        priceList.Id,
                        serviceProduct.Id,
                        unit?.Id,
                        tracing);

                if (priceListItem == null)
                {
                    throw new InvalidPluginExecutionException(
                        "No Price List Item was found for the selected Service and Price List.");
                }

                sellRate =
                    priceListItem
                        .GetAttributeValue<Money>(PriceListAmountField)
                        ?.Value ?? 0m;

                costRate =
                    priceListItem
                        .GetAttributeValue<Money>(PriceListCostField)
                        ?.Value ?? 0m;

                target[UnitAmountField] =
                    new Money(sellRate.Value);

                target[UnitCostField] =
                    new Money(costRate.Value);

                tracing.Trace(
                    "Service rates retrieved. Sell={0}, Cost={1}",
                    sellRate,
                    costRate);
            }


            // -----------------------------
            // 3. DURATION / PRICING
            // -----------------------------

            // Duration is stored in minutes.
            int durationMinutes =
                GetInt(
                    target,
                    preImage,
                    DurationField)
                ?? 0;

            // Example: 150 minutes × 2 techs = 300 technician minutes.
            int technicianMinutes =
                durationMinutes * numberOfTechs;

            
            decimal technicianHours =
                technicianMinutes / 60m;

            decimal billableHours =
                technicianHours;

            decimal totalSell =
                Math.Round(
                    billableHours * (sellRate ?? 0m),
                    2);

            decimal totalCost =
                Math.Round(
                    technicianHours * (costRate ?? 0m),
                    2);

            target[TotalSellPriceField] =
                new Money(totalSell);

            target[TotalCostField] =
                new Money(totalCost);

            tracing.Trace(
                "SERVICE RESULT: Duration={0}, Techs={1}, TechMinutes={2}, Hours={3}, SellRate={4}, CostRate={5}, TotalSell={6}, TotalCost={7}",
                durationMinutes,
                numberOfTechs,
                technicianMinutes,
                technicianHours,
                sellRate,
                costRate,
                totalSell,
                totalCost);
        }


        /// <summary>
        /// Agreement Booking Service
        /// -> Agreement Booking Incident
        /// -> Incident Type
        /// -> tb_numberoftechs
        /// </summary>
        private int? GetNumberOfTechsFromIncidentType(
            Entity target,
            Entity preImage,
            IOrganizationService service,
            ITracingService tracing)
        {
            EntityReference agreementBookingIncident =
                GetEntityReference(
                    target,
                    preImage,
                    AgreementBookingIncidentField);

            if (agreementBookingIncident == null)
            {
                tracing.Trace(
                    "Agreement Booking Incident missing. Cannot get # Techs.");
                return null;
            }

            Entity bookingIncident =
                service.Retrieve(
                    AgreementBookingIncidentTable,
                    agreementBookingIncident.Id,
                    new ColumnSet(IncidentTypeField));

            EntityReference incidentType =
                bookingIncident.GetAttributeValue<EntityReference>(
                    IncidentTypeField);

            if (incidentType == null)
            {
                tracing.Trace(
                    "Incident Type missing on Agreement Booking Incident.");
                return null;
            }

            Entity incidentTypeRecord =
                service.Retrieve(
                    IncidentTypeTable,
                    incidentType.Id,
                    new ColumnSet(IncidentTypeNumberOfTechsField));

            int? numberOfTechs =
                GetIntFromEntity(
                    incidentTypeRecord,
                    IncidentTypeNumberOfTechsField);

            if (!numberOfTechs.HasValue ||
                numberOfTechs.Value <= 0)
            {
                tracing.Trace(
                    "Incident Type has no valid # Techs. IncidentTypeId={0}",
                    incidentType.Id);

                return null;
            }

            tracing.Trace(
                "# Techs from Incident Type: {0}",
                numberOfTechs.Value);

            return numberOfTechs.Value;
        }


        /// <summary>
        /// Finds Price List Item by:
        /// Price List + Product/Service + Unit.
        /// </summary>
        private Entity GetPriceListItem(
            IOrganizationService service,
            Guid priceListId,
            Guid productOrServiceId,
            Guid? unitId,
            ITracingService tracing)
        {
            tracing.Trace(
                "Searching Price List Item. PriceList={0}, Product={1}, Unit={2}",
                priceListId,
                productOrServiceId,
                unitId);

            var query =
                new QueryExpression(PriceListItemTable)
                {
                    ColumnSet =
                        new ColumnSet(
                            PriceListAmountField,
                            PriceListCostField),

                    TopCount = 2
                };

            query.Criteria.AddCondition(
                PriceListItemPriceListField,
                ConditionOperator.Equal,
                priceListId);

            query.Criteria.AddCondition(
                PriceListItemProductField,
                ConditionOperator.Equal,
                productOrServiceId);

            if (unitId.HasValue)
            {
                query.Criteria.AddCondition(
                    PriceListItemUnitField,
                    ConditionOperator.Equal,
                    unitId.Value);
            }

            EntityCollection results =
                service.RetrieveMultiple(query);

            if (results.Entities.Count == 0)
            {
                tracing.Trace("No Price List Item found.");
                return null;
            }

            if (results.Entities.Count > 1)
            {
                throw new InvalidPluginExecutionException(
                    "Multiple matching Price List Items were found. " +
                    "Please verify the Price List, Product/Service, and Unit configuration.");
            }

            tracing.Trace(
                "Price List Item found: {0}",
                results.Entities[0].Id);

            return results.Entities[0];
        }


        // =========================================================
        // Helpers
        // Target = new/changed value.
        // PreImage = existing value when field is not in Target.
        // =========================================================

        private static object GetValue(
            Entity target,
            Entity preImage,
            string attributeName)
        {
            if (target != null &&
                target.Attributes.Contains(attributeName))
            {
                return target[attributeName];
            }

            if (preImage != null &&
                preImage.Attributes.Contains(attributeName))
            {
                return preImage[attributeName];
            }

            return null;
        }


        private static EntityReference GetEntityReference(
            Entity target,
            Entity preImage,
            string attributeName)
        {
            return GetValue(
                target,
                preImage,
                attributeName) as EntityReference;
        }


        private static int? GetInt(
            Entity target,
            Entity preImage,
            string attributeName)
        {
            object value =
                GetValue(
                    target,
                    preImage,
                    attributeName);

            return value == null
                ? (int?)null
                : Convert.ToInt32(value);
        }


        private static int? GetIntFromEntity(
            Entity entity,
            string attributeName)
        {
            if (entity == null ||
                !entity.Attributes.Contains(attributeName) ||
                entity[attributeName] == null)
            {
                return null;
            }

            return Convert.ToInt32(
                entity[attributeName]);
        }


        private static decimal? GetDecimal(
            Entity target,
            Entity preImage,
            string attributeName)
        {
            object value =
                GetValue(
                    target,
                    preImage,
                    attributeName);

            return value == null
                ? (decimal?)null
                : Convert.ToDecimal(value);
        }


        private static decimal? GetMoneyValue(
            Entity target,
            Entity preImage,
            string attributeName)
        {
            object value =
                GetValue(
                    target,
                    preImage,
                    attributeName);

            if (value == null)
            {
                return null;
            }

            if (value is Money money)
            {
                return money.Value;
            }

            return Convert.ToDecimal(value);
        }
    }
}