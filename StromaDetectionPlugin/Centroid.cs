/*
 * @author Sebastian Lohmann
 */
namespace Glaukopis.SharpAccessoryIntegration.Features {
	using System.Diagnostics.Contracts;
	using System.Drawing;
	using SharpAccessory.Imaging.Classification;
	using SharpAccessory.Imaging.Segmentation;
	public static class Centroid {
		public static void ProcessLayer(ObjectLayer layer,Point location) {
			Contract.Requires(null!=layer);
			for(var i=0;i<layer.Objects.Count;i++) {
				if(layer.Objects[i].Features.Contains(CentroidX.Name)&&layer.Objects[i].Features.Contains(CentroidY.Name)) continue;
				double[] centroid=Centroid.getCentroid(layer.Objects[i]);
				layer.Objects[i].Features.Add(new CentroidX(centroid[0]+location.X));
				layer.Objects[i].Features.Add(new CentroidY(centroid[1]+location.Y));
			}
		}
		public static void ProcessLayer(ObjectLayer layer) {
			Contract.Requires(null!=layer);
			ProcessLayer(layer,new Point(0,0));
		}
		private static double[] getCentroid(ImageObject imageObject) {
			Contract.Requires(null!=imageObject);
			imageObject.EnsureAssignedToLayer();
			var boundingBox=imageObject.Contour.FindBoundingBox();
			var centroid=new double[2];
			double area=0;
			for(var i=boundingBox.Y;i<boundingBox.Bottom;i++)
				for(var j=boundingBox.X;j<boundingBox.Right;j++)
					if(imageObject.Id==imageObject.Layer.Map[j,i]) {
						centroid[0]+=j;
						centroid[1]+=i;
						area++;
					}
			centroid[0]=(centroid[0]/area);
			centroid[1]=(centroid[1]/area);
			return centroid;
		}
	}
	public class CentroidX:Feature {
		public new const string Name="CentroidX";
		internal CentroidX(double x):base(CentroidX.Name,x){}
	}
	public class CentroidY:Feature {
		public new const string Name="CentroidY";
		internal CentroidY(double y):base(CentroidY.Name,y){}
	}
}
