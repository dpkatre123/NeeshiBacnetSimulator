using System;
using System.Collections.Generic;

namespace BacnetSim.Models
{
    /// <summary>
    /// Simple notification/alarm model used to describe who to contact and when.
    /// Includes a helper method that returns a sample notification configuration.
    /// </summary>
    public class BacnetNotification
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Condition expression used to trigger the notification. For this sample tool
        /// it's just a human-readable description (e.g. "PresentValue &gt; 30").
        /// </summary>
        public string TriggerCondition { get; set; } = string.Empty;

        /// <summary>Recipients for the notification (email addresses, user ids, etc.).</summary>
        public List<string> Recipients { get; set; } = new();

        /// <summary>Notification priority (higher = more urgent).</summary>
        public int Priority { get; set; } = 3;

        /// <summary>Method used to deliver notification (Email/SMS/BACnetAlarm).</summary>
        public DeliveryMethod Method { get; set; } = DeliveryMethod.BACnetAlarm;

        /// <summary>Optionally associate the notification with a schedule id to limit when it fires.</summary>
        public Guid? ScheduleId { get; set; }

        public static BacnetNotification Sample()
        {
            return new BacnetNotification
            {
                Name = "High Temperature Alert",
                Enabled = true,
                TriggerCondition = "PresentValue &gt;= 28.0",
                Recipients = new List<string> { "ops@example.com", "oncall@example.com" },
                Priority = 90,
                Method = DeliveryMethod.Email
            };
        }
    }

    public enum DeliveryMethod
    {
        Email,
        Sms,
        BACnetAlarm,
        Webhook
    }
}
