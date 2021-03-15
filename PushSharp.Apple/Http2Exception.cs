using System;
using PushSharp.Core;

namespace PushSharp.Apple
{
    public class ApnsHttp2NotificationException : NotificationException
    {
        public ApnsHttp2FailureReason ErrorStatusCode { get; set; }
        public new ApnsHttp2Notification Notification { get; set; }

        public ApnsHttp2NotificationException(ApnsHttp2FailureReason reason, ApnsHttp2Notification notification)
            : base($"Apns notification error: '{reason}'", notification)
        {
            ErrorStatusCode = reason;
            Notification = notification;
        }

        public ApnsHttp2NotificationException(ApnsHttp2FailureReason reason, ApnsHttp2Notification notification, Exception innerException)
            : base($"Apns notification error: '{reason}'", notification, innerException)
        {
            ErrorStatusCode = reason;
            Notification = notification;
        }
    }
}
