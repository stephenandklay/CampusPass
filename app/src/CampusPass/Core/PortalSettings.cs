using System;
using System.Xml.Serialization;

namespace CampusPass.Core
{
    /// <summary>
    /// Property declaration order is load-bearing: XmlSerializer emits elements
    /// in declaration order, and existing users already have settings.xml on disk
    /// written by the legacy build in exactly this order.
    /// </summary>
    [XmlRoot("PortalSettings", Namespace = "")]
    public sealed class PortalSettings
    {
        public string PortalUrl { get; set; }
        public string SubmitUrl { get; set; }
        public string Method { get; set; }
        public string UsernameField { get; set; }
        public string PasswordField { get; set; }
        public string ExtraFields { get; set; }
        public string ConnectivityUrl { get; set; }
        public string ConnectivityExpected { get; set; }
        public string SuccessKeywords { get; set; }
        public int CheckIntervalSeconds { get; set; }
        public bool DiscoverRedirect { get; set; }
        public bool AutoDetect { get; set; }

        public static PortalSettings ChinaMobileTemplate()
        {
            return new PortalSettings
            {
                PortalUrl = "http://211.143.60.126:8888/showLogin.do?wlanuserip={local_ip}&wlanacname=0042.0317.311.00",
                SubmitUrl = "http://211.143.60.126:8888/login.do",
                Method = "POST",
                UsernameField = "bpssUSERNAME",
                PasswordField = "bpssBUSPWD",
                ExtraFields = "showVerify=false\r\nloginType=1",
                ConnectivityUrl = "http://www.msftconnecttest.com/connecttest.txt",
                ConnectivityExpected = "Microsoft Connect Test",
                SuccessKeywords = "登录成功|您已成功登录",
                CheckIntervalSeconds = 60,
                DiscoverRedirect = true,
                AutoDetect = true
            };
        }

        public PortalSettings Clone()
        {
            return (PortalSettings)MemberwiseClone();
        }
    }
}
