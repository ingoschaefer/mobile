using System;

namespace Toggl.Phoebe
{
    public static class Build
    {
        #region Phoebe build config

        #if DEBUG
        public static readonly Uri ApiUrl = new Uri ("https://toggl.com/api/");
        public static readonly Uri ReportsApiUrl = new Uri ("https://toggl.com/reports/api/");
        #else
        public static readonly Uri ApiUrl = new Uri ("https://toggl.com/api/");
        public static readonly Uri ReportsApiUrl = new Uri ("https://toggl.com/reports/api/");
        #endif
        public static readonly Uri PrivacyPolicyUrl = new Uri ("https://toggl.com/privacy");
        public static readonly Uri TermsOfServiceUrl = new Uri ("https://toggl.com/terms");
        public static readonly string GoogleAnalyticsId = "UA-3215787-23";
        public static readonly int GoogleAnalyticsPlanIndex = 1;
        public static readonly int GoogleAnalyticsExperimentIndex = 2;

        #endregion

        #region Joey build configuration

        #if __ANDROID__
        public static readonly string AppIdentifier = "TogglJoey";
        public static readonly string GcmSenderId = "426090949585";
        public static readonly string BugsnagApiKey = "fa53459a137c6f3abc9486fcca4a53d8";
        public static readonly string XamInsightsApiKey = "349d73fb2c25630636295b83c47bbef984b25c78";
        public static readonly string GooglePlayUrl = "https://play.google.com/store/apps/details?id=com.toggl.timer";
        #endif
        #endregion

        #region Ross build configuration

        #if __IOS__
        public static readonly string AppStoreUrl = "itms-apps://itunes.com/apps/toggl";
        public static readonly string AppIdentifier = "TogglRoss";
        public static readonly string BugsnagApiKey = "fa53459a137c6f3abc9486fcca4a53d8";
        public static readonly string GoogleOAuthClientId = "426090949585-o507qe17gt35eud4pbtvl8dicc1t83qm.apps.googleusercontent.com";
        #endif
        #endregion
    }
}
