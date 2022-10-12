using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Net;
using HttpTwo;
using System.Threading;
using PushSharp.Core;

namespace PushSharp.Apple
{
    public class ApnsHttp2Connection
    {
        static int ID = 0;

        public ApnsHttp2Connection(ApnsHttp2Configuration configuration)
        {
            id = ++ID;
            if (id >= int.MaxValue)
                ID = 0;

            Configuration = configuration;

            certificate = Configuration.Certificate;
            currentKeepAlivePeriod = Configuration.KeepAlivePeriod;

            certificates = new X509CertificateCollection();

            // Add local/machine certificate stores to our collection if requested
            if (Configuration.AddLocalAndMachineCertificateStores)
            {
                var store = new X509Store(StoreLocation.LocalMachine);
                certificates.AddRange(store.Certificates);

                store = new X509Store(StoreLocation.CurrentUser);
                certificates.AddRange(store.Certificates);
            }

            // Add optionally specified additional certs into our collection
            if (Configuration.AdditionalCertificates != null)
            {
                foreach (var addlCert in Configuration.AdditionalCertificates)
                    certificates.Add(addlCert);
            }

            // Finally, add the main private cert for authenticating to our collection
            if (certificate != null)
                certificates.Add(certificate);

            var http2Settings = new HttpTwo.Http2ConnectionSettings(
                Configuration.Host,
               (uint)Configuration.Port,
                true,
                certificates);

            http2 = new HttpTwo.Http2Client(http2Settings);

            // Initialize timer to try and keep the connection alive
            if (Configuration.KeepAlivePeriod != null && Configuration.TryKeepingConnectionAlive)
            {
                Log.Info("Initializing Kepp Alive Timer");

                keepAliveStartTimeSpan = TimeSpan.FromSeconds(30);

                keepAliveTimer = new Timer(e => Ping(), null, keepAliveStartTimeSpan, Configuration.KeepAlivePeriod);
            }
        }

        public ApnsHttp2Configuration Configuration { get; private set; }

        X509CertificateCollection certificates;
        X509Certificate2 certificate;
        int id = 0;
        HttpTwo.Http2Client http2;
        Timer keepAliveTimer;
        TimeSpan keepAliveStartTimeSpan;
        TimeSpan currentKeepAlivePeriod;

        private readonly object _syncObj = new object();


        public async Task Send(ApnsHttp2Notification notification)
        {
            var url = string.Format("https://{0}:{1}/3/device/{2}",
                          Configuration.Host,
                          Configuration.Port,
                          notification.DeviceToken);
            var uri = new Uri(url);

            var payload = notification.Payload.ToString();

            var data = Encoding.UTF8.GetBytes(payload);

            var headers = new NameValueCollection();
            headers.Add("apns-id", notification.Uuid); // UUID

            if (notification.Expiration.HasValue)
            {
                var sinceEpoch = notification.Expiration.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                var secondsSinceEpoch = (long)sinceEpoch.TotalSeconds;
                headers.Add("apns-expiration", secondsSinceEpoch.ToString()); //Epoch in seconds
            }

            if (notification.Priority.HasValue)
                headers.Add("apns-priority", notification.Priority == ApnsPriority.Low ? "5" : "10"); // 5 or 10

            headers.Add("content-length", data.Length.ToString());

            if (!string.IsNullOrEmpty(notification.Topic))
                headers.Add("apns-topic", notification.Topic); // string topic

            Http2Client.Http2Response response;
            lock (_syncObj)
            {
                response = http2.Post(uri, headers, data).Result;
            }

            if (response.Status == HttpStatusCode.OK)
            {
                // Check for matching uuid's
                var responseUuid = response.Headers["apns-id"];
                if (responseUuid != notification.Uuid)
                    throw new Exception("Mismatched APNS-ID header values");
            }
            else
            {
                // Try parsing json body
                var json = new JObject();

                if (response.Body != null && response.Body.Length > 0)
                {
                    var body = Encoding.ASCII.GetString(response.Body);
                    json = JObject.Parse(body);
                }

                if (response.Status == HttpStatusCode.Gone)
                {

                    var timestamp = DateTime.UtcNow;
                    if (json != null && json["timestamp"] != null)
                    {
                        var sinceEpoch = json.Value<long>("timestamp");
                        timestamp = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sinceEpoch);
                    }

                    // Expired
                    throw new PushSharp.Core.DeviceSubscriptonExpiredException(notification)
                    {
                        OldSubscriptionId = notification.DeviceToken,
                        NewSubscriptionId = null,
                        ExpiredAt = timestamp
                    };
                }

                // Get the reason
                var reasonStr = json.Value<string>("reason");

                var reason = (ApnsHttp2FailureReason)Enum.Parse(typeof(ApnsHttp2FailureReason), reasonStr, true);

                throw new ApnsHttp2NotificationException(reason, notification);
            }
        }

        private bool Ping()
        {
            // Stop and dispose timer
            if (!Configuration.TryKeepingConnectionAlive)
            {
                Log.Info("TryKeepingConnectionAlive is FALSE -> exiting & disposing");

                keepAliveTimer.Dispose();
                return false;
            }

            Log.Debug("Starting ping execution");
            var data = Encoding.UTF8.GetBytes("PINGPONG");

            var cancelTokenSource = new CancellationTokenSource();
            cancelTokenSource.CancelAfter(TimeSpan.FromSeconds(2));

            var pong = http2.Ping(data, cancelTokenSource.Token).Result;
            Log.Info($"Ping result={pong}");

            // Change timer period
            if (Configuration.KeepAlivePeriod != currentKeepAlivePeriod)
            {
                if (keepAliveTimer.Change(keepAliveStartTimeSpan, Configuration.KeepAlivePeriod))
                {
                    currentKeepAlivePeriod = Configuration.KeepAlivePeriod;
                    Log.Info($"Kepp Alive Timer - Changed");
                }
            }

            return pong;
        }
    }
}

