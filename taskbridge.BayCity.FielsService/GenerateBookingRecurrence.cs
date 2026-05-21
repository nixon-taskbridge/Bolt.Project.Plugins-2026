using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taskbridge.BayCity.FielsService
{
    public class GenerateBookingRecurrence : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target) || target.LogicalName != "msdyn_agreement")
                    return;

                Entity agreement = service.Retrieve("msdyn_agreement", target.Id, new ColumnSet("tb_servicefrequency", "msdyn_startdate", "msdyn_enddate", "statecode", "msdyn_systemstatus"));

                if (agreement.GetAttributeValue<OptionSetValue>("statecode")?.Value != 0 ||
                    agreement.GetAttributeValue<OptionSetValue>("msdyn_systemstatus")?.Value != 690970001)
                    return;

                int frequency = agreement.GetAttributeValue<OptionSetValue>("tb_servicefrequency")?.Value ?? -1;
                DateTime startDate = agreement.GetAttributeValue<DateTime>("msdyn_startdate");
                DateTime endDate = agreement.GetAttributeValue<DateTime>("msdyn_enddate");
                // DateTime adjustedStart = GetStartDateByFrequency(startDate, frequency);
                // int intervalMonths = GetMonthIntervalByFrequency(frequency);
                // int dayOfMonth = GetValidDayOfMonth(adjustedStart);

                EntityCollection setups = RetrieveBookingSetups(service, agreement.Id, frequency);

                foreach (Entity setup in setups.Entities)
                {
                    try
                    {
                        string workOrderTypeName = setup.GetAttributeValue<EntityReference>("msdyn_workordertype")?.Name ?? string.Empty;

                        // For Major setup, use agreement start date and 12-month interval
                        DateTime adjustedStart = workOrderTypeName.Equals("Major PM", StringComparison.OrdinalIgnoreCase)
                            ? startDate
                            : GetStartDateByFrequency(startDate, frequency);

                        int intervalMonths = workOrderTypeName.Equals("Major PM", StringComparison.OrdinalIgnoreCase)
                            ? 12
                            : GetMonthIntervalByFrequency(frequency);

                        int dayOfMonth = GetValidDayOfMonth(adjustedStart);
                        // Ensure day of the month is not before the start date, that will delay the booking date
                        // This is especially important when the original start date falls on a weekend,
                        // and the recurrence day is adjusted backward (e.g., from Saturday to Friday).
                        // If that adjusted day ends up earlier than the agreement start date,
                        // the system skips to the next interval month to generate the first booking correctly.

                        DateTime adjustStart_New = new DateTime(adjustedStart.Year, adjustedStart.Month, dayOfMonth); // t
                        string xml = BuildRecurrenceXml(adjustStart_New, endDate, intervalMonths, dayOfMonth);

                        Entity e = new Entity("msdyn_agreementbookingsetup")
                        {
                            Id = setup.Id,
                            ["msdyn_recurrencesettings"] = xml,

                        };

                        e["tb_createbookingdatesmanually"] = true;

                        service.Update(e);
                    }
                    catch (Exception ex)
                    {
                        tracingService.Trace("Error processing booking setup ID {0}: {1}", setup.Id, ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Error in GenerateBookingRecurrenceXML: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("An error occurred while generating recurrence XML.", ex);
            }
            
        }
        /// <summary>
        /// Returns the number of months to repeat based on service frequency.
        /// </summary>
        private int GetMonthIntervalByFrequency(int frequency)
        {
            switch (frequency)
            {
                case 126700002: return 1;  // Monthly
                case 126700003: return 3;  // Quarterly
                case 126700001: return 12;  // Semi-Annual,For multi-year agreements, each subsequent minor service should occur exactly one year after the initial minor service.
                    //For example, in a 2 - year semi - annual agreement:
                    //Major 1: 1/1/2026 ,Minor 1: 7/1/2026,Major 2: 1/1/2027,Minor 2: 7/1/2027
                case 126700000: return 12; // Annual
                default: return 12;        // Default to Annual if unknown
            }
        }

        /// <summary>
        /// Ensures the recurrence day is valid for the month and not on a weekend.
        /// </summary>
        private int GetValidDayOfMonth(DateTime adjustedStart)
        {
            //try
            //{
            //    int day = adjustedStart.Day;
            //    int daysInMonth = DateTime.DaysInMonth(adjustedStart.Year, adjustedStart.Month);

            //    if (day > daysInMonth)
            //        day = daysInMonth;

            //    DayOfWeek dow = new DateTime(adjustedStart.Year, adjustedStart.Month, day).DayOfWeek;
            //    if (dow == DayOfWeek.Saturday) day -= 1;
            //    else if (dow == DayOfWeek.Sunday) day += 1;

            //    // Adjust if the new day is 0 (e.g., 1st falls on Saturday)
            //    if (day < 1)
            //    {
            //        day += 2;

            //    }
            //    else if (day > daysInMonth)
            //    {
            //        day = daysInMonth;
            //    }

            //    return day;
            //}
            //catch
            //{
                return 1; // default to 1st always 1st day of the month
            //}
        }

        /// <summary>
        /// Builds recurrence XML string based on monthly pattern.
        /// </summary>
        private string BuildRecurrenceXml(DateTime start, DateTime end, int intervalMonths, int dayOfMonth)
        {
            try
            {
                return $@"<root>
  <pattern>
    <period>monthly</period>
    <option>every</option>
    <months every='{intervalMonths}'><day>{dayOfMonth}</day></months>
  </pattern>
  <range>
    <start>{start:MM/dd/yyyy}</start>
    <option>endBy</option>
    <end>{end:MM/dd/yyyy}</end>
  </range>
  <datas/>
</root>";
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Adjusts the start date forward based on the frequency interval.
        /// </summary>
        private DateTime GetStartDateByFrequency(DateTime baseDate, int frequency)
        {
            try
            {
                int monthsToAdd = 0;

                switch (frequency)
                {
                    case 126700001: // Semi-Annual
                        monthsToAdd = 6;
                        break;
                    case 126700002: // Monthly
                        monthsToAdd = 1;
                        break;
                    case 126700003: // Quarterly
                        monthsToAdd = 3;
                        break;
                    default: // Annual or unknown
                        monthsToAdd = 0;
                        break;
                }

                return baseDate.AddMonths(monthsToAdd);
            }
            catch
            {
                return baseDate;
            }
        }

        /// <summary>
        /// Retrieves related booking setups (Major/Minor) based on frequency.
        /// </summary>
        private EntityCollection RetrieveBookingSetups(IOrganizationService service, Guid agreementId, int frequency)
        {
            try
            {
                QueryExpression query = new QueryExpression("msdyn_agreementbookingsetup")
                {
                    ColumnSet = new ColumnSet("msdyn_agreementbookingsetupid", "msdyn_workordertype")
                };
                query.Criteria.AddCondition("msdyn_agreement", ConditionOperator.Equal, agreementId);

                FilterExpression filter = new FilterExpression(LogicalOperator.Or);
                Guid major = GetWorkOrderTypeId(service, "Major PM");
                Guid minor = GetWorkOrderTypeId(service, "Minor PM");

                if (frequency == 126700000 && major != Guid.Empty)
                {
                    filter.AddCondition("msdyn_workordertype", ConditionOperator.Equal, major);
                }
                else
                {
                    if (major != Guid.Empty)
                        filter.AddCondition("msdyn_workordertype", ConditionOperator.Equal, major);
                    if (minor != Guid.Empty)
                        filter.AddCondition("msdyn_workordertype", ConditionOperator.Equal, minor);
                }

                if (filter.Conditions.Count > 0)
                    query.Criteria.AddFilter(filter);

                return service.RetrieveMultiple(query);
            }
            catch
            {
                return new EntityCollection();
            }
        }

        /// <summary>
        /// Gets the GUID for a given work order type name.
        /// </summary>
        private Guid GetWorkOrderTypeId(IOrganizationService service, string name)
        {
            try
            {
                QueryExpression query = new QueryExpression("msdyn_workordertype")
                {
                    ColumnSet = new ColumnSet("msdyn_workordertypeid")
                };
                query.Criteria.AddCondition("msdyn_name", ConditionOperator.Equal, name);
                EntityCollection results = service.RetrieveMultiple(query);
                return results.Entities.Count > 0 ? results.Entities[0].Id : Guid.Empty;
            }
            catch
            {
                return Guid.Empty;
            }
        }
    
    }
}
