using System;
using System.Collections.Generic;
using System.Drawing;
using SharpAccessory.Imaging.Automation;
using SharpAccessory.Imaging.Classification;
using SharpAccessory.Imaging.Classification.Features.Color;
using SharpAccessory.Imaging.Classification.Features.Gradient;
using SharpAccessory.Imaging.Filters;
using SharpAccessory.Imaging.Processors;
using SharpAccessory.Imaging.Segmentation;
using SharpAccessory.Threading;
namespace Glaukopis.SharpAccessoryIntegration.Segmentation{
	public class CellCoreSegmentation:LocalProcessBase
	{
		private const int MAX_CONTOURLENGTH=500;
		private const int MIN_CONTOURLENGTH=25;
		private const int MIN_AREA=25;


		public CellCoreSegmentation() : base("Image Analysis") { }
	
	
		public override ProcessResult Execute(ProcessExecutionParams p)
		{
			GrayscaleProcessor gpH=new ColorDeconvolution().Get1stStain(p.Bitmap, ColorDeconvolution.KnownStain.HaematoxylinEosin);
		
			GrayscaleProcessor gp=new GrayscaleProcessor(p.Bitmap, RgbToGrayscaleConversion.JustReds);
		
			GrayscaleProcessor gpSobel=(GrayscaleProcessor)gp.Clone();
		
			new MeanFilter().Execute(gp, new Size(3, 3));
		
			new SobelEdgeDetector().Execute(gpSobel);
		
			new MinimumFilter().Execute(gpH, new Size(3, 3));
		
			new PixelInverter().Execute(gpH);
		
			ObjectLayer l1stLevel=this.Execute1stLevelSegmentation(gp, gpSobel, gpH);
		
			float targetArea=this.GetTargetArea(l1stLevel);
		
			ObjectLayer l2ndLevel=this.Execute2ndLevelSegmentation(gp, gpSobel, gpH, targetArea);
		
			ObjectLayer l3rdLevel=this.Execute3rdLevelSegmentation(l2ndLevel, gpSobel, gpH, targetArea);
		
			gpSobel.WriteBack=false;
			gpH.WriteBack=false;
			gp.WriteBack=false;
		
			gpSobel.Dispose();
			gpH.Dispose();
			gp.Dispose();
		
			gpSobel.Bitmap.Dispose();
			gpH.Bitmap.Dispose();
		
			l3rdLevel.Name="Image Analysis";
		
			return new ProcessResult(new ObjectLayer[]{l3rdLevel});
		}
	
	
		private ObjectLayer Execute3rdLevelSegmentation(ObjectLayer l2ndLevel, GrayscaleProcessor gpSobel, GrayscaleProcessor gpH, float targetArea)
		{
			List<Contour> finalContours=new List<Contour>();
		
			for(int i=0; i<l2ndLevel.Objects.Count; i++)
			{
				finalContours.Add(l2ndLevel.Objects[i].Contour);
			}
		
			double[] hBackground=this.GetBackgroundHistogram(l2ndLevel, gpH);
			double[] hForeground=this.GetForegroundHistogram(l2ndLevel, gpH);
		
			Parallel.For(0, l2ndLevel.Objects.Count, i=>
			{
				ImageObject obj=l2ndLevel.Objects[i];
			
				ContourProperties cp=ContourProperties.FromContour(obj.Contour);
			
				obj.Features.Add(new Feature("Area", cp.Area));
			});
		
			Map map=new Map(gpSobel.Width, gpSobel.Height);
		
			Parallel.For(0, gpH.Height, dy=>
			{
				for(int dx=0; dx<gpH.Width; dx++)
				{
					UInt32 h=gpH[dx, dy];
				
					if(hForeground[h]<=hBackground[h]) continue;
				
					UInt32 id=l2ndLevel.Map[dx, dy];
				
					if(id!=0)
					{
						ImageObject obj=l2ndLevel.Objects.GetObjectById(id);
					
						double area=obj.Features["Area"].Value;
					
						if(area>0.33*targetArea) continue;
					}
				
					map[dx, dy]=0xffffffff;
				}
			});
		
			ObjectLayer layer=new ConnectedComponentCollector().Execute(map);
		
			layer=new ContourOptimizer().RemoveNonCompactPixels(layer, 3);
		
			for(int i=0; i<layer.Objects.Count; i++)
			{
				finalContours.Add(layer.Objects[i].Contour);
			}
		
			Contour[] contours=this.Sort(finalContours.ToArray(), gpSobel, gpH, targetArea);
		
			layer=this.CreateLayer(gpSobel.Width, gpSobel.Height, contours);
		
			Map finalMap=new Map(layer.Map, false);
		
			for(int dy=0; dy<gpH.Height; dy++) for(int dx=0; dx<gpH.Width; dx++)
			{
				if(l2ndLevel.Map[dx, dy]!=0) continue;
			
				if(map[dx, dy]!=0) continue;
			
				finalMap[dx, dy]=0;
			}
		
			layer=new ConnectedComponentCollector().Execute(finalMap);
		
			layer=new ContourOptimizer().RemoveNonCompactPixels(layer, 3);
		
			layer=new ConcaveObjectSeparation().Execute(layer, 0.33, true);
		
			double minArea=Math.Max(0.1*targetArea, MIN_AREA);
		
			layer=layer.CreateAbove(obj=>
			{
				float area=ContourProperties.FromContour(obj.Contour).Area;
			
				return area>minArea;
			});
		
			layer=this.RefillContours(layer);
		
			return layer;
		}
	
	
		private ObjectLayer CreateLayer(int width, int height, Contour[] contours)
		{
			Map map=new Map(width, height);
		
			UInt32 id=1;
		
			for(int i=0; i<contours.Length; i++)
			{
				Contour c=contours[i];
			
				if(c.Length<MIN_CONTOURLENGTH) continue;
			
				c.Fill(map, id, false);
			
				id++;
			}
		
			return new ConnectedComponentCollector().Execute(map);
		}
	
	
		private Contour[] Sort(Contour[] contours, GrayscaleProcessor gpSobel, GrayscaleProcessor gpH, float targetArea)
		{
			List<ContourDataComposite<double>> valuedContours=new List<ContourDataComposite<double>>();
		
			Parallel.For(0, contours.Length, i=>
			{
				Contour c=contours[i];
			
				double value=this.GetContourValue(c, gpSobel, gpH, targetArea);
			
				if(value<=0) return;
			
				lock(valuedContours)
				{
					valuedContours.Add(new ContourDataComposite<double>(c, value));
				}
			});
		
			valuedContours.Sort(delegate(ContourDataComposite<double> c1, ContourDataComposite<double> c2)
			{
				if(c1.Data>c2.Data) return -1;
				if(c1.Data<c2.Data) return 1;
			
				return 0;
			});
		
			contours=new Contour[valuedContours.Count];
		
			for(int i=0; i<valuedContours.Count; i++)
			{
				contours[i]=valuedContours[i].Contour;
			}
		
			return contours;
		}
	
	
		private ObjectLayer Execute2ndLevelSegmentation(GrayscaleProcessor gp, GrayscaleProcessor gpSobel, GrayscaleProcessor gpH, float targetArea)
		{
			ContourBasedSegmentation cbs=new ContourBasedSegmentation();
		
			cbs.CreatePrimarySegmentation(gp, MAX_CONTOURLENGTH);
		
			cbs.EvaluateContours(c=>
			{
				return this.GetContourValue(c, gpSobel, gpH, targetArea);
			});
		
			ObjectLayer layer=cbs.CreateLayer(MIN_CONTOURLENGTH, int.MaxValue);
		
			layer=layer.CreateAbove(obj=>
			{
				return this.GetContourGradient(obj, gp)<0;
			});
		
			layer=new ContourOptimizer().RemoveNonCompactPixels(layer, 3);
		
			layer=new ConcaveObjectSeparation().Execute(layer, 0.33, true);
		
			double[] hBackground=this.GetBackgroundHistogram(layer, gpH);
			double[] hForeground=this.GetForegroundHistogram(layer, gpH);
		
			bool isFirst=true;
		
			ObjectLayer firstStep=null;
		
			while(true)
			{
				bool removed=false;
			
				layer=layer.CreateAbove(obj=>
				{
					int[] hHistogram=this.GetHistogram(obj, gpH);
				
					double hRatioForeground=this.GetRatioForeground(hHistogram, hForeground, hBackground);
				
					if(hRatioForeground>0.5) return true;
				
					for(int i=0; i<256; i++)
					{
						int val=hHistogram[i];
					
						hForeground[i]-=val;
						hBackground[i]+=val;
					}
				
					removed=true;
				
					return false;
				});
			
				if(isFirst)
				{
					firstStep=layer;
					isFirst=false;
				}
			
				if(!removed) break;
			}

			if(layer.Objects.Count==0) layer=firstStep;
		
			double minArea=Math.Max(0.1*targetArea, MIN_AREA);
		
			layer=layer.CreateAbove(obj=>
			{
				float area=ContourProperties.FromContour(obj.Contour).Area;
			
				return area>=minArea;
			});
		
			layer=this.RefillContours(layer);
		
			return layer;
		}
	
	
		private ObjectLayer RefillContours(ObjectLayer layer)
		{
			Map map=new Map(layer.Map.Width, layer.Map.Height);
		
			for(int i=0; i<layer.Objects.Count; i++)
			{
				ImageObject obj=layer.Objects[i];
			
				obj.Contour.Fill(map, obj.Id, true);
			}
		
			layer=new ConnectedComponentCollector().Execute(map);
		
			return layer;
		}
	
	
		private double GetRatioForeground(int[] histogram, double[] foreground, double[] background)
		{
			double sumForeground=0.0;
			double sumBackground=0.0;
		
			for(int i=0; i<256; i++)
			{
				int value=histogram[i];
			
				double cleanForeground=foreground[i]-value;
			
				if(cleanForeground>background[i])
				{
					sumForeground+=value;
				}
				else sumBackground+=value;
			}
		
			return sumForeground/(sumForeground+sumBackground);
		}
	
	
		private double[] GetForegroundHistogram(ObjectLayer layer, GrayscaleProcessor p)
		{
			double[] histogram=new double[256];
		
			Parallel.For(0, layer.Objects.Count, i=>
			{
				int[] objHistogram=this.GetHistogram(layer.Objects[i], p);
			
				lock(histogram)
				{
					for(int j=0; j<256; j++)
					{
						int value=objHistogram[j];
					
						histogram[j]+=value;
					}
				}
			});
		
			return histogram;
		}
	
	
		private double[] GetBackgroundHistogram(ObjectLayer layer, GrayscaleProcessor p)
		{
			Map map=new Map(p.Width, p.Height);
		
			for(int dy=0; dy<p.Height; dy++) for(int dx=0; dx<p.Width; dx++)
			{
				if(layer.Map[dx, dy]==0) map[dx, dy]=1;
			}
		
			double[,] distanceMap=new DistanceTransformation().Execute(map);
		
			double[] histogram=new double[256];
		
			for(int dy=0; dy<p.Height; dy++) for(int dx=0; dx<p.Width; dx++)
			{
				if(map[dx, dy]==0) continue;
			
				if(distanceMap[dx, dy]<3) continue;
			
				histogram[p[dx, dy]]++;
			}
		
			return histogram;
		}
	
	
		private int[] GetHistogram(ImageObject obj, GrayscaleProcessor p)
		{
			Rectangle boundingBox=obj.Contour.FindBoundingBox();
		
			int y2=boundingBox.Bottom;
			int x2=boundingBox.Right;
			int y1=boundingBox.Y;
			int x1=boundingBox.X;
		
			int[] histogram=new int[256];
		
			for(int dy=y1; dy<y2; dy++) for(int dx=x1; dx<x2; dx++)
			{
				if(obj.Layer.Map[dx, dy]!=obj.Id) continue;
			
				histogram[p[dx, dy]]++;
			}
		
			return histogram;
		}
	
	
		private double GetContourValue(Contour c, GrayscaleProcessor gpSobel, GrayscaleProcessor gpH, float targetArea)
		{
			double h=MeanIntensityOnContour.GetValue(c, gpH);
		
			ContourProperties cp=ContourProperties.FromContour(c);
		
			float area=Math.Min(cp.Area, targetArea)/targetArea;
		
			if(cp.Convexity<0.9 && cp.Area<targetArea)
			{
				area=1F/targetArea;
			}
		
			float convexity=cp.Convexity*cp.Convexity;
		
			double color=h/255.0;
		
			return color*area*convexity*ContourValue.GetValue(c, gpSobel);
		}
	
	
		private float GetTargetArea(ObjectLayer layer)
		{
			if(layer.Objects.Count==0) return -1;
		
			float targetArea=-1F;
		
			List<float> values=new List<float>();
		
			for(int i=0; i<layer.Objects.Count; i++)
			{
				ImageObject obj=layer.Objects[i];
			
				float area=ContourProperties.FromContour(obj.Contour).Area;
			
				values.Add(area);
			}
		
			values.Sort();
		
			int index=(int)(0.95*values.Count);
		
			if(values.Count>0) targetArea=values[index];
		
			return targetArea;
		}
	
	
		private ObjectLayer Execute1stLevelSegmentation(GrayscaleProcessor gp, GrayscaleProcessor gpSobel, GrayscaleProcessor gpH)
		{
			ContourBasedSegmentation cbs=new ContourBasedSegmentation();
		
			cbs.CreatePrimarySegmentation(gp, MAX_CONTOURLENGTH);
		
			cbs.EvaluateContours(c=>
			{
				if(ContourProperties.FromContour(c).Convexity<0.95) return -1;
			
				return ContourValue.GetValue(c, gpSobel);
			});
		
			ObjectLayer layer=cbs.CreateLayer(MIN_CONTOURLENGTH, int.MaxValue);
		
			layer=new ContourOptimizer().RemoveNonCompactPixels(layer, 3);
		
			layer=layer.CreateAbove(obj=>
			{
				return this.GetContourGradient(obj, gp)<0;
			});
		
			layer=new ConcaveObjectSeparation().Execute(layer, 0.33, true);
		
			return layer;
		}
	
	
		private double GetContourGradient(ImageObject obj, GrayscaleProcessor gp)
		{
			int[] nX={ +1, 0, -1, 0 };
			int[] nY={ 0, +1, 0, -1 };
		
			int height=gp.Height;
			int width=gp.Width;
		
			double sumInner=0.0, sumOuter=0.0;
			double numInner=0.0, numOuter=0.0;
		
			for(int i=0; i<obj.Contour.Length; i++)
			{
				Point p=obj.Contour[i];
			
				for(int j=0; j<4; j++)
				{
					int x=p.X+nX[j];
					int y=p.Y+nY[j];
				
					if(x<0 || y<0 || x>=width || y>=height) continue;
				
					if(obj.Contour.Contains(x, y)) continue;
				
					UInt32 intensity=gp[x, y];
				
					if(obj.Layer.Map[x, y]==obj.Id)
					{
						sumInner+=intensity;
						numInner++;
					}
					else
					{
						sumOuter+=intensity;
						numOuter++;
					}
				}
			}
		
			double meanInner=sumInner/numInner;
			double meanOuter=sumOuter/numOuter;
		
			return meanInner-meanOuter;
		}

	}
}