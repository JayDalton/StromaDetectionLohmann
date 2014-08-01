/*
 * @author Sebastian Lohmann
 */
namespace StromaDetectionPlugin {
	using System;
	using System.Drawing;
	using System.Linq;
	using System.Windows.Forms;
	using Glaukopis.Core.Analysis;
	using Glaukopis.SharpAccessoryIntegration.Filter;
	using Glaukopis.SharpAccessoryIntegration.Segmentation;
	using SharpAccessory.CognitionMaster.Plugging;
	using SharpAccessory.Imaging.Automation;
	using SharpAccessory.Imaging.Filters;
	using SharpAccessory.Imaging.Processors;
	using SharpAccessory.Imaging.Segmentation;
	using SharpAccessory.VisualComponents.Dialogs;
	[PluginDefaultEnabled(true)]
	class StromaDetectionPlugin:Plugin {
		private Bitmap source,stainH,stainE,edges;
		private SobelResponse responseH,responseE;
		private double[,] nonMaximumSupression;
		private NumericUpDown threshold;
		protected override void OnLoad(EventArgs e) {
			base.OnLoad(e);
			this.CreateTabContainer("StromaDetection");
			this.TabContainer.Enabled=true;
			(new Button { Text="execute cell core segmentation",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate{
				if(null==this.DisplayedImage) return;
				ProcessResult result=null;
				var progressDialog=new ProgressDialog {Message="executing cell core segmentation",ProgressBarStyle=ProgressBarStyle.Marquee,AllowCancel=false};
				progressDialog.BackgroundTask+=()=> {
					var segmentation=new CellCoreSegmentation();
					var executionParams=new ProcessExecutionParams(this.DisplayedImage);
					result=segmentation.Execute(executionParams);
				};
				progressDialog.CenterToScreen();
				progressDialog.ShowDialog();
				this.SetLayers(result.Layers.ToArray());
			};
			(new Button { Text="execute threshold segmentation",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate {
				if(null==this.DisplayedImage) return;
				var m=new Map(this.DisplayedImage.Width,this.DisplayedImage.Height);
				using(var gp=new GrayscaleProcessor(this.DisplayedImage.Clone() as Bitmap,RgbToGrayscaleConversion.Mean))
					for(var x=0;x<this.DisplayedImage.Width;x++)
						for(var y=0;y<this.DisplayedImage.Height;y++)
							m[x,y]=gp.GetPixel(x,y)<this.threshold.Value?1u:0u;
				var layer=new ConnectedComponentCollector().Execute(m);
				layer.Name="threshold "+this.threshold.Value+" segmentation";
				var layers=this.GetLayers().ToList();
				layers.Add(layer);
				this.SetLayers(layers.ToArray());
			};
			this.threshold=new NumericUpDown { Parent=new GroupBox { Parent=this.TabContainer,Dock=DockStyle.Top,Text="threshold",Height=40 },Dock=DockStyle.Fill,Minimum=0,Maximum=255,Increment=16,Value=128,DecimalPlaces=0 };
			(new Button { Text="display edges",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate { this.SetDisplayedImage(this.edges); };
			(new Button { Text="execute edge detection",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate {
				if(null==this.stainH||null==this.stainE) return;
				this.responseH=Filtering.ExecuteSobel(this.stainH);
				this.responseE=Filtering.ExecuteSobel(this.stainE);
				var substracted=new double[this.responseH.Size.Width,this.responseH.Size.Height];
				var substractedRange=new Range<double>();
				for(var x=0;x<this.responseH.Size.Width;x++)
					for(var y=0;y<this.responseH.Size.Height;y++){
						var value=Math.Max(0,this.responseE.Gradient[x,y]-this.responseH.Gradient[x,y]);
						substracted[x,y]=value;
						substractedRange.Add(value);
					}
				this.nonMaximumSupression=Filtering.ExecuteNonMaximumSupression(substracted,this.responseE.Orientation);
				this.edges=Visualization.Visualize(this.nonMaximumSupression,Visualization.CreateColorizing(substractedRange.Maximum));
				this.SetDisplayedImage(this.edges);
			};
			(new Button { Text="display haematoxylin",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate { this.SetDisplayedImage(this.stainH); };
			(new Button { Text="display eosin",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate { this.SetDisplayedImage(this.stainE); };
			(new Button { Text="display source",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate { this.SetDisplayedImage(this.source); };
			(new Button { Text="execute deconvolution",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate {
				if(null==this.DisplayedImage) return;
				this.source=this.DisplayedImage;
				var gpE=new ColorDeconvolution().Get2ndStain(this.DisplayedImage,ColorDeconvolution.KnownStain.HaematoxylinEosin);
				gpE.Dispose();
				this.stainE=gpE.Bitmap;
				var gpH=new ColorDeconvolution().Get1stStain(this.DisplayedImage,ColorDeconvolution.KnownStain.HaematoxylinEosin);
				gpH.Dispose();
				this.stainH=gpH.Bitmap;
				this.SetDisplayedImage(this.stainE);
			};
		}
	}
}
