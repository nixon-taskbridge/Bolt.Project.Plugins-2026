using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Taskbridge.BayCity.FielsService
{
    public class AgreementQuote_MarginCalculations : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            try
            {
                // Ensure the plugin is executed on Update and target exists
                if ((context.MessageName != "Update") || !context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
                {
                    return;
                }

                // Retrieve the Agreement Quote record
                Entity agreementQuote = (Entity)context.InputParameters["Target"];

               
               // if (agreementQuote.Contains("tb_originalagreementprice") )
                //{


                    // Call method to calculate margins
                    CalculateMarginAmounts(service, agreementQuote.Id, tracingService);
                //}
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in Execute method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred in the AgreementQuote_MarginCal plugin: {ex.Message}", ex);
            }
        }
        private void CalculateMarginAmounts(IOrganizationService service, Guid agreementQuoteId, ITracingService tracingService)
        {
            try
            {
                Entity agreementQuote = service.Retrieve("tb_agreementquote", agreementQuoteId, new ColumnSet(true));
                // Retrieve necessary fields with null checks
                decimal originalPrice = agreementQuote.Contains("tb_originalagreementprice") ? agreementQuote.GetAttributeValue<Money>("tb_originalagreementprice").Value : 0m;
                decimal markupPercentage = agreementQuote.Contains("tb_markup") ? agreementQuote.GetAttributeValue<decimal>("tb_markup") : 0m;
                decimal discountPercentage = agreementQuote.Contains("tb_discount") ? agreementQuote.GetAttributeValue<decimal>("tb_discount") : 0m;
                decimal taxPercentage = agreementQuote.Contains("tb_tax") ? agreementQuote.GetAttributeValue<decimal>("tb_tax") : 0m;

                // Calculate markup, discount and tax amounts
                decimal markupAmount = originalPrice * (markupPercentage / 100);
                decimal discountAmount = originalPrice * (discountPercentage / 100);
                decimal priceAfterMarkupDiscount = originalPrice + markupAmount - discountAmount;
                decimal taxAmount = priceAfterMarkupDiscount * (taxPercentage / 100);

                // Calculate Total Agreement Price after markup andd discount
                decimal totalAgreementPrice = priceAfterMarkupDiscount + taxAmount;

                //calculate Labor and Products Profit Margin.
                decimal productsTotalCost = agreementQuote.Contains("tb_productstotalcost") ? agreementQuote.GetAttributeValue<Money>("tb_productstotalcost").Value : 0m;
                decimal productsTotalPrice = agreementQuote.Contains("tb_productstotalprice") ? agreementQuote.GetAttributeValue<Money>("tb_productstotalprice").Value : 0m;
                decimal servicesTotalCost = agreementQuote.Contains("tb_servicetotalcost") ? agreementQuote.GetAttributeValue<Money>("tb_servicetotalcost").Value : 0m;
                decimal servicesTotalPrice = agreementQuote.Contains("tb_servicetotalprice") ? agreementQuote.GetAttributeValue<Money>("tb_servicetotalprice").Value : 0m;

                // Adjust products and services total price with markup and discount
                decimal adjustedProductsTotalPrice = productsTotalPrice * (1 + markupPercentage / 100) * (1 - discountPercentage / 100);
                decimal adjustedServicesTotalPrice = servicesTotalPrice * (1 + markupPercentage / 100) * (1 - discountPercentage / 100);

                // Calculate Profit Margins
                decimal laborProfitMarginPercentage = adjustedServicesTotalPrice != 0
                                                       ? (adjustedServicesTotalPrice - servicesTotalCost) / adjustedServicesTotalPrice * 100:0;
                decimal productsProfitMarginPercentage = adjustedProductsTotalPrice != 0
                                                          ? (adjustedProductsTotalPrice - productsTotalCost) / adjustedProductsTotalPrice * 100:0;

                decimal laborProfitMargin = (adjustedServicesTotalPrice - servicesTotalCost);
                decimal productsProfitMargin = (adjustedProductsTotalPrice - productsTotalCost);


                Entity e = new Entity("tb_agreementquote");
                e.Id = agreementQuoteId;
                // Set the Total Agreement Price on the entity
                e["tb_totalagreementprice"] = new Money(totalAgreementPrice);
                e["tb_laborprofitmargin"] = laborProfitMargin;
                e["tb_productsprofitmargin"] = productsProfitMargin;
                e["tb_laborprofitmarginpercentage"] = laborProfitMarginPercentage;
                e["tb_productsprofitmarginpercentage"] = productsProfitMarginPercentage;

                service.Update(e);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Exception in GetAssetTotals method: {0}", ex.ToString());
                throw new InvalidPluginExecutionException($"An error occurred while calculating Amounts: {ex.Message}", ex);
            }

        }
    }
   
}
