using Android.App;
using Android.Content;
using Android.OS;

namespace PhiInfo.Android
{
    [Activity(Exported = true, NoHistory = true, Theme = "@android:style/Theme.Translucent.NoTitleBar")]
    [IntentFilter([Intent.ActionView],
        Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
        DataScheme = "phiinfo")]
    public class PhiInfoSchemeActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var uri = Intent?.Data;
            if (uri != null && uri.Scheme == "phiinfo")
            {
                var command = uri.Host;
                var serviceIntent = new Intent(this, typeof(HttpServerService));

                if (command == "start")
                {
                    StartForegroundService(serviceIntent);
                }
                else if (command == "exit")
                {
                    StopService(serviceIntent);
                }
            }

            Finish();
        }
    }
}