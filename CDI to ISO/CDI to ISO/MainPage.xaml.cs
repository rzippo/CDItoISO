using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Services.Store;
using Microsoft.Services.Store.Engagement;
using Windows.Storage;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CDI_to_ISO
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public StorageFile CdiFile { get; set; }
        public StorageFile IsoFile { get; set; }

        private readonly Progress<int> progress;

        private CancellationTokenSource tokenSource = new CancellationTokenSource();

        private readonly StoreServicesCustomEventLogger logger = StoreServicesCustomEventLogger.GetDefault();

        public MainPage()
        {
            InitializeComponent();

            #if DEBUG
            ApplicationData.Current.ClearAsync(ApplicationDataLocality.Roaming).AsTask().Wait();
            #endif

            progress = new Progress<int>(
                percent => ConversionProgressBar.Value = percent);

            ApplicationView.PreferredLaunchViewSize = new Size(700, 500);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if(e.Parameter is IActivatedEventArgs args)
            {
                if(args.Kind == ActivationKind.File)
                {
                    if (args is FileActivatedEventArgs fileArgs)
                    {
                        CdiFile = (StorageFile) fileArgs.Files[0];
                        CdiPathBox.Text = CdiFile.Name;
                    }
                }
            }
        }

        private async void CdiSelectButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                FileTypeFilter = {".cdi"}
            };

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                CdiFile = file;
                CdiPathBox.Text = file.Name;
            }
        }

        private async void IsoSelectButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = CdiFile?.DisplayName ?? ""
            };
            picker.FileTypeChoices.Add("ISO image file", new List<string>(){ ".iso" });

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                IsoFile = file;
                IsoPathBox.Text = file.Name;
            }
        }

        private void ClearCdiFile()
        {
            CdiFile = null;
            CdiPathBox.Text = "";
        }

        private void ClearIsoFile()
        {
            IsoFile = null;
            IsoPathBox.Text = "";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            tokenSource.Cancel();
            tokenSource.Dispose();
            tokenSource = new CancellationTokenSource();
        }

        private async void ConvertButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            if(CdiFile != null && IsoFile != null)
            {
                ConversionProgressBar.Visibility = Visibility.Visible;
                
                CancelButton.IsEnabled = true;
                ConvertButton.IsEnabled = false;
                
                ConversionResult conversionResult = await Task.Run(() => Cdi2IsoConverter.ConvertAsync(
                    CdiFile,
                    IsoFile,
                    progress,
                    LogViewer.LogWriter,
                    token: tokenSource.Token
                ));

                await ProcessConversionResult(conversionResult);
            }
        }

        private async Task ShowSimpleContentDialog(string message, string closeButtonText = "Ok", string title = null)
        {
            ContentDialog contentDialog = new ContentDialog
            {
                Content = message
            };

            if (title != null)
                contentDialog.Title = title;

            if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog", nameof(ContentDialog.CloseButtonText)))
            {
                contentDialog.CloseButtonText = closeButtonText;
            }
            else
            {
                contentDialog.SecondaryButtonText = closeButtonText;
            }

            await contentDialog.ShowAsync();
        }

        private async Task ProcessConversionResult(ConversionResult conversionResult)
        {
            switch (conversionResult)
            {
                case ConversionResult.Success:
                    logger.Log("Conversion.Success");
                    await ShowSimpleContentDialog("Conversion completed!");
                    break;

                case ConversionResult.ConversionCanceled:
                    logger.Log("Conversion.Canceled");
                    await ShowSimpleContentDialog("Conversion canceled by user.");
                    break;

                case ConversionResult.IoException:
                    logger.Log("Conversion.IOException");
                    await ShowSimpleContentDialog("Exception while accessing files. Conversion aborted.");
                    break;

                default:
                    logger.Log("Conversion.FailedUnknown");
                    await ShowSimpleContentDialog("Conversion failed.");
                    break;
            }

            if (conversionResult == ConversionResult.Success)
            {
                await AskForReview();
                ClearCdiFile();
                ClearIsoFile();
            }

            CancelButton.IsEnabled = false;
            ConvertButton.IsEnabled = true;
        }
        
        private async Task AskForReview()
        {
            object hasGivenReviewObject = ApplicationData.Current.RoamingSettings.Values["HasGivenReview"];
            bool hasGivenReview = hasGivenReviewObject is bool b && b;

            if (!hasGivenReview)
            {
                object popupRefusedCountObject = ApplicationData.Current.RoamingSettings.Values["popupRefusedCount"];
                int popupRefusedCount = popupRefusedCountObject is int i ? i : 1;
                if(popupRefusedCount > 1)
                {
                    Random rng = new Random();
                    if (rng.NextDouble() > 1.0 / popupRefusedCount)
                    {
                        return;
                    }
                }

                ContentDialog wantToReviewDialog = new ContentDialog
                {
                    Title = "Are you liking CDI to ISO?",
                    Content = "Please consider to leave a feedback on the Store",
                    PrimaryButtonText = "Rate now",
                };

                if (ApiInformation.IsPropertyPresent("Windows.UI.Xaml.Controls.ContentDialog",
                    nameof(ContentDialog.CloseButtonText)))
                {
                    wantToReviewDialog.CloseButtonText = "Later";
                }
                else
                {
                    wantToReviewDialog.SecondaryButtonText = "Later";
                } 

                ContentDialogResult result = await wantToReviewDialog.ShowAsync();
                if(result == ContentDialogResult.Primary)
                {
                    bool reviewResult = await ShowRatingReviewDialog();
                    ApplicationData.Current.RoamingSettings.Values["HasGivenReview"] = reviewResult;
                }
                else
                {
                    logger.Log("Review.Refused");
                    ApplicationData.Current.RoamingSettings.Values["popupRefusedCount"] = popupRefusedCount + 1;
                }
            }
        }

        public async Task<bool> ShowRatingReviewDialog()
        {
            if(ApiInformation.IsMethodPresent(typeof(StoreRequestHelper).FullName, nameof(StoreRequestHelper.SendRequestAsync), 3))
            {
                logger.Log("Review.SendRequest");
                StoreSendRequestResult result = await StoreRequestHelper.SendRequestAsync(
                    StoreContext.GetDefault(), 16, string.Empty);

                if (result.ExtendedError == null)
                {
                    JObject jsonObject = JObject.Parse(result.Response);
                    if (jsonObject.SelectToken("status").ToString() == "success")
                    {
                        // The customer rated or reviewed the app.
                        return true;
                    }
                }

                // There was an error with the request, or the customer chose not to
                // rate or review the app.
                return false;
            }
            else
            {
                logger.Log("Review.LaunchUri");
                bool reviewResult = await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-windows-store://review/?ProductId=9PCZBVLLDSX4"));
                return reviewResult;
            }
        }

        private async void Info_OnClick(object sender, RoutedEventArgs e)
        {
            logger.Log("InfoPanel.Opened");
            var infoDialog = new InfoDialog();
            await infoDialog.ShowAsync();
        }

        private void ShowLogCheck_Change(object sender, RoutedEventArgs e)
        {
            if (ShowLogCheck.IsChecked ?? false)
            {
                logger.Log("Log.Shown");
                LogViewer.Visibility = Visibility.Visible;
            }
            else
            {
                logger.Log("Log.Hidden");
                LogViewer.Visibility = Visibility.Collapsed;
            }
        }
    }
}
