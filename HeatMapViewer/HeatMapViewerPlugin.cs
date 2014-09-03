/*
 * @author Sebastian Lohmann
 */
using Glaukopis.CognitionMasterIntegration;
using Glaukopis.SlideProcessing;
using SharpAccessory.CognitionMaster.Plugging;
using SharpAccessory.CognitionMaster.WholeSlideImageSupport;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Glaukopis.CognitionMasterPlugins
{
  using Glaukopis.CognitionMasterIntegration;
  using Glaukopis.SlideProcessing;
  using SharpAccessory.CognitionMaster.Plugging;
  using SharpAccessory.CognitionMaster.WholeSlideImageSupport;
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.IO;
  using System.Windows.Forms;

  [PluginDefaultEnabled(true)]
  public class HeatMapViewerPlugin : Plugin
  {
    private Bitmap heatMap;
    private PictureBox pictureBox;
    private Processing dataHolder;
    private ComboBox comboBox;
    private readonly Dictionary<string, string> heatMapNames = new Dictionary<string, string>();

    protected override void OnLoad(EventArgs e)
    {
      base.OnLoad(e);
      this.CreateTabContainer("HeatMapViewer");
      this.TabContainer.Enabled = true;
      this.pictureBox = new PictureBox { Parent = this.TabContainer, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
      this.pictureBox.Click += this.pictureBox_Click;
      this.comboBox = new ComboBox { Parent = this.TabContainer, Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
      this.comboBox.SelectedValueChanged += this.comboBox_SelectedValueChanged;
    }

    void pictureBox_Click(object sender, EventArgs e)
    {
      var mouseEventArgs = e as MouseEventArgs;
      if (null == mouseEventArgs) return;
      if (MouseButtons.Left != mouseEventArgs.Button) return;
      var controlCoordinate = new Point(mouseEventArgs.X, mouseEventArgs.Y);
      var imageCoordinate = this.pictureBox.GetImageCoordinates(controlCoordinate);
      var target = new PointF(imageCoordinate.X / this.widthRatio, imageCoordinate.Y / this.heightRatio);
      WsiInterop.Navigation.Goto(WsiInterop.Navigation.Zoom, target);
    }

    void comboBox_SelectedValueChanged(object sender, EventArgs e)
    {
      var item = this.comboBox.SelectedItem as string;
      if (null == item || !this.heatMapNames.ContainsKey(item)) return;
      var heatMapFileName = this.heatMapNames[item];
      this.heatMap = Image.FromFile(heatMapFileName) as Bitmap;
      if (null == this.heatMap) return;
      this.pictureBox.Image = this.heatMap.Clone() as Bitmap;
      this._widthRatio = null;
      this._heightRatio = null;
      this.wsiInteropNavigationChanged(sender, e);
    }

    protected override void OnImageFileOpened(EventArgs e)
    {
      base.OnImageFileOpened(e);
      this._widthRatio = null;
      this._heightRatio = null;
      if (null == this.ImageFile) return;
      this.dataHolder = new Processing(this.ImageFile.Url);
      this.comboBox.Items.Clear();
      this.heatMapNames.Clear();
      if (null != this.heatMap) this.heatMap.Dispose();
      this.heatMap = null;
      if (null != this.pictureBox.Image) this.pictureBox.Image.Dispose();
      this.pictureBox.Image = null;
      WsiInterop.Navigation.Changed += this.wsiInteropNavigationChanged;
      foreach (var hm in Directory.GetFiles(this.dataHolder.DataPath, "*.png"))
      {
        var i1 = hm.LastIndexOf('\\');
        var n = hm.Substring(i1 + 1);
        var i2 = n.LastIndexOf('.');
        n = n.Remove(i2);
        this.heatMapNames.Add(n, hm);
        this.comboBox.Items.Add(n);
      }
    }

    protected override void OnImageFileClosed(EventArgs e)
    {
      base.OnImageFileClosed(e);
      WsiInterop.Navigation.Changed -= this.wsiInteropNavigationChanged;
    }

    private void wsiInteropNavigationChanged(object sender, EventArgs eventArgs)
    {
      if (null == this.heatMap) return;
      var viewArea = WsiInterop.Navigation.SrcRectangle;
      var image = this.heatMap.Clone() as Bitmap;
      using (var gfx = Graphics.FromImage(image))
      using (var pen = new Pen(Color.Wheat))
        gfx.DrawRectangle(pen, viewArea.X * this.widthRatio, viewArea.Y * this.heightRatio, viewArea.Width * this.widthRatio, viewArea.Height * this.heightRatio);
      this.pictureBox.Image.Dispose();
      this.pictureBox.Image = image;
    }

    private float? _widthRatio;
    private float widthRatio
    {
      get
      {
        if (!this._widthRatio.HasValue)
        {
          this._widthRatio = this.heatMap.Width / (float)WsiInterop.Wsi.Size.Width;
        }
        return this._widthRatio.Value;
      }
    }

    private float? _heightRatio;
    private float heightRatio
    {
      get
      {
        if (!this._heightRatio.HasValue) 
        { 
          this._heightRatio = this.heatMap.Height / (float)WsiInterop.Wsi.Size.Height; 
        }
        return this._heightRatio.Value;
      }
    }
  }
}
