using Microsoft.Graphics.Canvas;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace InkingPrototype
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        VM viewModel;
        RandomAccessStreamReference image;

        public MainPage()
        {
            this.InitializeComponent();
            viewModel = new VM();
            this.DataContext = viewModel;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            this.inkCanvas.InkPresenter.InputDeviceTypes = Windows.UI.Core.CoreInputDeviceTypes.Pen | Windows.UI.Core.CoreInputDeviceTypes.Mouse | Windows.UI.Core.CoreInputDeviceTypes.Touch;
            DataTransferManager.GetForCurrentView().DataRequested += MainPage_DataRequested;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DataTransferManager.GetForCurrentView().DataRequested -= MainPage_DataRequested;
        }

        private void MainPage_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            viewModel.Status = "DataRequested Start";
            viewModel.Status += $"\nDeadline: {args.Request.Deadline.ToString()}";

            args.Request.Data.Properties.Description = "Shared drwaing";
            args.Request.Data.Properties.Title = "Sdílíme ink";
            args.Request.Data.SetDataProvider(StandardDataFormats.Bitmap, renderer);

            viewModel.Status += "\nDataRequested End";
        }

        private async void renderer(DataProviderRequest request)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () => {
                var deferral = request.GetDeferral();
                request.SetData(await GetImage());
                deferral.Complete();
            });
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }

        async public Task<RandomAccessStreamReference> GetImage()
        {
            // 1. Příprava
            CanvasDevice device = CanvasDevice.GetSharedDevice();

            // 2. Získáme pozadí jako bitmapu
            RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap();
            await renderTargetBitmap.RenderAsync(sourceGrid, (int)sourceGrid.ActualWidth, (int)sourceGrid.ActualHeight);

            // 3. Zafixujeme rozměry bitmapy v pixelech
            var bitmapSizeAt96Dpi = new Size(renderTargetBitmap.PixelWidth, renderTargetBitmap.PixelHeight);

            // 4. Získáme pixely
            IBuffer pixelBuffer = await renderTargetBitmap.GetPixelsAsync();
            byte[] pixels = pixelBuffer.ToArray();

            // 5. Začneme renderovat při 96 DPI
            using (CanvasRenderTarget renderTarget = new CanvasRenderTarget(device, (int)sourceGrid.ActualWidth, (int)sourceGrid.ActualHeight, 96.0f))
            {
                using (var drawingSession = renderTarget.CreateDrawingSession())
                {
                    using (var win2dRenderedBitmap = CanvasBitmap.CreateFromBytes(device, pixels,
                                                        (int)bitmapSizeAt96Dpi.Width, (int)bitmapSizeAt96Dpi.Height,
                                                        Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                                                        96.0f))
                    {
                        drawingSession.DrawImage(win2dRenderedBitmap,
                            new Rect(0, 0, renderTarget.SizeInPixels.Width, renderTarget.SizeInPixels.Height),
                            new Rect(0, 0, bitmapSizeAt96Dpi.Width, bitmapSizeAt96Dpi.Height));
                    }

                    // 6. Přidáme ink
                    drawingSession.Units = CanvasUnits.Pixels;
                    drawingSession.DrawInk(inkCanvas.InkPresenter.StrokeContainer.GetStrokes());
                }

                var outputBitmap = new SoftwareBitmap(
                    BitmapPixelFormat.Bgra8,
                    (int)renderTarget.SizeInPixels.Width,
                    (int)renderTarget.SizeInPixels.Height,
                    BitmapAlphaMode.Premultiplied);

                outputBitmap.CopyFromBuffer(renderTarget.GetPixelBytes().AsBuffer());

                // 7. Vykreslíme do bufferu
                var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                                    (uint)renderTarget.SizeInPixels.Width, (uint)renderTarget.SizeInPixels.Height,
                                    96.0f, 96.0f,
                                    renderTarget.GetPixelBytes()
                );
                await encoder.FlushAsync();
                stream.Seek(0);

                return RandomAccessStreamReference.CreateFromStream(stream);
            }
        }
    }

    public class VM : INotifyPropertyChanged
    {
        private string status;

        public string Status
        {
            get { return status; }
            set { Set<string>(ref status, value); }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        public void Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (!Equals(storage, value))
            {
                storage = value;
                RaisePropertyChanged(propertyName);
            }
        }

        public void RaisePropertyChanged([CallerMemberName] string propertyName = null) =>
           PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #endregion
    }
}
