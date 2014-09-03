/*
 * @author Sebastian Lohmann
 */
using Glaukopis.Adapters.R;
using Glaukopis.Core.Analysis;
using Glaukopis.SlideProcessing;
using RDotNet;
using SharpAccessory.CognitionMaster.WholeSlideImageSupport;
using SharpAccessory.Imaging.Filters;
using SharpAccessory.VirtualMicroscopy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Deconvolution
{
  static class Deconvolution
  {
    private static void Main(string[] args)
    {
      #region init
      if (0 == args.Length)
      {
        Console.WriteLine("no slide name");
        return;
      }
      var slideName = args[0];
      var processinHelper = new Processing(slideName);
      var slide = processinHelper.Slide;
      #endregion init
      
      TiledProcessInformation<uint[]> haematoxylinHistogram;
      TiledProcessInformation<uint[]> eosinHistogram;

      if (!File.Exists(processinHelper.DataPath + "haematoxylinHistogram.tpi") || !File.Exists(processinHelper.DataPath + "eosinHistogram.tpi"))
      {
        if (!Directory.Exists(processinHelper.DataPath + "deconvolution")) Directory.CreateDirectory(processinHelper.DataPath + "deconvolution");
        var tissueData = TiledProcessInformation<bool>.FromFile(processinHelper.DataPath + "tissueData.tpi");
        haematoxylinHistogram = new TiledProcessInformation<uint[]>(tissueData.Partitioner, tissueData.WsiUri);
        eosinHistogram = new TiledProcessInformation<uint[]>(tissueData.Partitioner, tissueData.WsiUri);
        var partitioner = tissueData.Partitioner;
        var dict = new Dictionary<Point, WsiRect>();
        foreach (var tile in tissueData.Partitioner)
          dict.Add(tissueData.Partitioner.CurrentIndices, tile);
        var stopwatch = new Stopwatch();
        foreach (var tile in partitioner)
        {
          var tissueTile = dict[partitioner.CurrentIndices];
          if (!tissueData[tissueTile]) continue;
          stopwatch.Start();
          using (var tileImage = slide.GetImagePart(tile))
          {
            var hHistogramValue = new uint[256];
            var hValues = new List<double>();
            using (var gpH = new ColorDeconvolution().Get1stStain(tileImage, ColorDeconvolution.KnownStain.HaematoxylinEosin))
            {
              foreach (var intensity in gpH.GetPixels())
              {
                hHistogramValue[intensity]++;
                hValues.Add(intensity);
              }
              haematoxylinHistogram.AddDataToCurrentTile(hHistogramValue);
              gpH.Dispose();
              gpH.Bitmap.Save(processinHelper.DataPath + "deconvolution\\" + partitioner.CurrentIndices + ".h.png");
            }
            var eHistogramValue = new uint[256];
            var eValues = new List<double>();
            using (var gpE = new ColorDeconvolution().Get2ndStain(tileImage, ColorDeconvolution.KnownStain.HaematoxylinEosin))
            {
              foreach (var intensity in gpE.GetPixels())
              {
                eHistogramValue[intensity]++;
                eValues.Add(intensity);
              }
              eosinHistogram.AddDataToCurrentTile(eHistogramValue);
              gpE.Dispose();
              gpE.Bitmap.Save(processinHelper.DataPath + "deconvolution\\" + partitioner.CurrentIndices + ".e.png");
            }
            NumericVector hHistogram = RConnector.Engine.CreateNumericVector(hValues);
            RConnector.Engine.SetSymbol("hHistogram", hHistogram);
            NumericVector eHistogram = RConnector.Engine.CreateNumericVector(eValues);
            RConnector.Engine.SetSymbol("eHistogram", eHistogram);
            var handle = RConnector.StartOutput();
            RConnector.Engine.Evaluate("hist(eHistogram, col=rgb(1,0,0,0.5),xlim=c(0,255), main=\"" + partitioner.CurrentIndices + "\", xlab=\"HE\")");
            RConnector.Engine.Evaluate("hist(hHistogram, col=rgb(0,0,1,0.5), add=T)");
            var output = RConnector.EndOutput(handle);
            output.Save(processinHelper.DataPath + "deconvolution\\histogram" + partitioner.CurrentIndices + ".png");
          }
          stopwatch.Stop();
          Console.WriteLine(partitioner.CurrentIndices + ":" + (stopwatch.ElapsedMilliseconds / 1000d) + "s");
          stopwatch.Reset();
        }
        haematoxylinHistogram.ToFile(processinHelper.DataPath + "haematoxylinHistogram.tpi");
        eosinHistogram.ToFile(processinHelper.DataPath + "eosinHistogram.tpi");
      }
      else
      {
        haematoxylinHistogram = TiledProcessInformation<uint[]>.FromFile(processinHelper.DataPath + "haematoxylinHistogram.tpi");
        eosinHistogram = TiledProcessInformation<uint[]>.FromFile(processinHelper.DataPath + "eosinHistogram.tpi");
      }

      var hRange = new Range<uint>();
      foreach (var tile in haematoxylinHistogram.Partitioner)
      {
        if (null == haematoxylinHistogram[tile]) continue;
        uint sum = 0;
        for (uint i = 0; i < 256; i++)
        {
          sum += haematoxylinHistogram[tile][i] * (255 - i);
        }
        hRange.Add(sum);
      }
      Func<uint[], Color> h2pixel = h =>
      {
        if (null == h) return Color.Gray;
        uint sum = 0;
        for (uint i = 0; i < 256; i++)
        {
          sum += h[i] * (255 - i);
        }
        var ratio = (double)sum / hRange.Maximum;
        return Color.FromArgb(0, 0, (int)Math.Round(255.0 * ratio));
      };
      var eRange = new Range<uint>();
      foreach (var tile in eosinHistogram.Partitioner)
      {
        if (null == eosinHistogram[tile]) continue;
        uint sum = 0;
        for (uint i = 0; i < 256; i++)
        {
          sum += eosinHistogram[tile][i] * (255 - i);
        }
        eRange.Add(sum);
      }
      Func<uint[], Color> e2pixel = e =>
      {
        if (null == e) return Color.Gray;
        uint sum = 0;
        for (uint i = 0; i < 256; i++)
        {
          sum += e[i] * (255 - i);
        }
        var ratio = (double)sum / eRange.Maximum;
        return Color.FromArgb((int)Math.Round(255.0 * ratio), 0, 0);
      };
      using (Bitmap b = haematoxylinHistogram.GenerateHeatMap(h2pixel))
        b.Save(processinHelper.DataPath + "haematoxylinHistogram.png");
      using (Bitmap b = eosinHistogram.GenerateHeatMap(e2pixel))
        b.Save(processinHelper.DataPath + "eosinHistogram.png");
      Console.WriteLine("done");
      Console.ReadKey();
    }
  }
}
