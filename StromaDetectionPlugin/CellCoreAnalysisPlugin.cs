/*
 * @author Sebastian Lohmann
 */
namespace StromaDetectionPlugin {
	using Glaukopis.Adapters.R;
	using Glaukopis.SharpAccessoryIntegration.Features;
	using Glaukopis.SharpAccessoryIntegration.Segmentation;
	using RDotNet;
	using SharpAccessory.CognitionMaster.Plugging;
	using SharpAccessory.Imaging.Automation;
	using SharpAccessory.Imaging.Classification;
	using SharpAccessory.Imaging.Classification.Features.Localization;
	using SharpAccessory.Imaging.Classification.Features.Shape;
	using SharpAccessory.Imaging.Segmentation;
	using SharpAccessory.VisualComponents.Dialogs;
	using System;
	using System.Collections.Generic;
	using System.Drawing;
	using System.Globalization;
	using System.Windows.Forms;
	[PluginDefaultEnabled(true)]
	class CellCoreAnalysisPlugin:Plugin{
		private NumericUpDown clusters;
		protected override void OnLoad(EventArgs e) {
			base.OnLoad(e);
			this.CreateTabContainer("StromaDetection2");
			this.TabContainer.Enabled=true;
			RConnector.BinPath=@"C:\Program Files\R\R-3.1.0\bin\x64\";
			(new Button { Text="cluster by roundness",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate {
				if(null==this.SelectedLayer) return;
				try { RConnector.Engine.Evaluate("library(\"mclust\")"); } catch { throw new Exception("try install mclust"); }
				var progressDialog=new ProgressDialog { Message="extracting features",ProgressBarStyle=ProgressBarStyle.Marquee,AllowCancel=false };
				progressDialog.BackgroundTask+=() => {
					var matrix=RConnector.Engine.CreateNumericMatrix(this.SelectedLayer.Objects.Count,1);
					for(var i=0;i<this.SelectedLayer.Objects.Count;i++) {
						matrix[i,0]=this.SelectedLayer.Objects[i].Features["AspectRatioOfBoundingEllipsoid"].Value;
					}
					var result=this.cluster(matrix);
					this.drawClusters(result.Item1,result.Item2);
				};
				progressDialog.CenterToScreen();
				progressDialog.ShowDialog();
				this.RefreshBuffer();
			};
			(new Button { Text="cluster by position",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate {
				if(null==this.SelectedLayer) return;
				try{ RConnector.Engine.Evaluate("library(\"mclust\")"); }
				catch{ throw new Exception("try install mclust"); }
				var progressDialog=new ProgressDialog { Message="extracting features",ProgressBarStyle=ProgressBarStyle.Marquee,AllowCancel=false };
				progressDialog.BackgroundTask+=() => {
					var matrix=RConnector.Engine.CreateNumericMatrix(this.SelectedLayer.Objects.Count,2);
					for(var i=0;i<this.SelectedLayer.Objects.Count;i++) {
						var io=this.SelectedLayer.Objects[i];
						matrix[i,0]=io.Features["CentroidX"].Value;
						matrix[i,1]=io.Features["CentroidY"].Value;
					}
					var result=this.cluster(matrix);
					this.drawClusters(result.Item1,result.Item2);
				};
				progressDialog.CenterToScreen();
				progressDialog.ShowDialog();
				this.RefreshBuffer();
			};
			this.clusters=new NumericUpDown { Parent=new GroupBox { Parent=this.TabContainer,Dock=DockStyle.Top,Text="clusters",Height=40 },Dock=DockStyle.Fill,Minimum=0,Maximum=20,Increment=1,Value=0,DecimalPlaces=0 };
			(new Button { Text="extract features",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate {
				if(null==this.SelectedLayer) return;
				var progressDialog=new ProgressDialog { Message="extracting features",ProgressBarStyle=ProgressBarStyle.Marquee,AllowCancel=false };
				progressDialog.BackgroundTask+=() => {
					Centroid.ProcessLayer(this.SelectedLayer);
					foreach(var io in this.SelectedLayer.Objects) {
						var boundingEllipsoid=BoundingEllipsoid.FromContour(io.Contour);
						if(!io.Features.Contains("AngleOfBoundingEllipsoid")) io.Features.Add(new AngleOfBoundingEllipsoid(boundingEllipsoid));
						if(!io.Features.Contains("AspectRatioOfBoundingEllipsoid")) io.Features.Add(new AspectRatioOfBoundingEllipsoid(boundingEllipsoid));
					}
				};
				progressDialog.CenterToScreen();
				progressDialog.ShowDialog();
			};
			(new Button { Text="execute cell core segmentation",Parent=this.TabContainer,Dock=DockStyle.Top }).Click+=delegate {
				if(null==this.DisplayedImage) return;
				ProcessResult result=null;
				var progressDialog=new ProgressDialog { Message="executing cell core segmentation",ProgressBarStyle=ProgressBarStyle.Marquee,AllowCancel=false };
				progressDialog.BackgroundTask+=() => {
					var segmentation=new CellCoreSegmentation();
					var executionParams=new ProcessExecutionParams(this.DisplayedImage);
					result=segmentation.Execute(executionParams);
				};
				progressDialog.CenterToScreen();
				progressDialog.ShowDialog();
				this.SetLayers(result.Layers.ToArray());
			};
		}
		private Tuple<NumericVector,NumericVector> cluster(NumericMatrix matrix){
			RConnector.Engine.SetSymbol("dataMatrix",matrix);
			if(0==this.clusters.Value) {
				RConnector.Engine.Evaluate("dataCluster<-Mclust(dataMatrix)");
				var clusterAssignment=RConnector.Engine.Evaluate("dataCluster$classification").AsNumeric();
				return Tuple.Create<NumericVector,NumericVector>(clusterAssignment,null);
			} else {
				RConnector.Engine.Evaluate("dataMeans<-kmeans(dataMatrix,"+this.clusters.Value+")");
				var clusterAssignment=RConnector.Engine.Evaluate("dataMeans$cluster").AsNumeric();
				var clusterInfo=RConnector.Engine.Evaluate("dataMeans$withinss").AsNumeric();
				return Tuple.Create(clusterAssignment,clusterInfo);
			}
		}
		private void drawClusters(NumericVector clusterAssignment,NumericVector clusterInfo) {
			var classes=new Dictionary<int,Class>();
			var random=new Random();
			for(var i=0;i<this.SelectedLayer.Objects.Count;i++) {
				var io=this.SelectedLayer.Objects[i];
				var cluster=(int)clusterAssignment[i];
				if(!classes.ContainsKey(cluster)){
					if(null!=clusterInfo) classes.Add(cluster,new Class(cluster+": "+clusterInfo[cluster-1].ToString(CultureInfo.InvariantCulture),Color.FromArgb(random.Next(255),random.Next(255),random.Next(255))));
					else classes.Add(cluster,new Class(cluster.ToString(CultureInfo.InvariantCulture),Color.FromArgb(random.Next(255),random.Next(255),random.Next(255))));
				}
				io.Class=classes[cluster];
			}
		}
	}
}
