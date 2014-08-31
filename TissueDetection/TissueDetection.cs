/*
 * @author Sebastian Lohmann & Björn Lindequist
 */
namespace SlideProcessing
{
  using System;
  using System.Drawing;
  using System.Linq;
  using Glaukopis.SlideProcessing;
  using SharpAccessory.Imaging.DigitalPathology;
  using SharpAccessory.Imaging.Processors;
  using SharpAccessory.Imaging.Segmentation;
  using SharpAccessory.VirtualMicroscopy;

  static class TissueDetection
  {
    private const int OverviewTileSize = 128;
    private const int Radius = 2;
    private const int NoiseLevel = 10;

    static void Main(string[] args)
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

      var part = new WsiPartitioner(new Rectangle(new Point(0, 0), slide.Size), new Size(1000, 1000), new Size(0, 0), 1.0);
      var tissueData = new TiledProcessInformation<bool>(part, slideName);
      
      #region global tissue detection
      int overviewImageWidth = slide.Size.Width / OverviewTileSize;
      int overviewImageHeight = slide.Size.Height / OverviewTileSize;

      Bitmap overviewImage = slide.GetImagePart(0, 0, slide.Size.Width, slide.Size.Height, overviewImageWidth, overviewImageHeight);

      var bitmapProcessor = new BitmapProcessor(overviewImage);

      ObjectLayer overviewLayer = new TissueDetector().Execute(bitmapProcessor, Radius, NoiseLevel);

      bitmapProcessor.Dispose();

      DrawObjectsToImage(overviewImage, overviewLayer, Color.Black);
      overviewImage.Save(processinHelper.DataPath + "tissueDetectionOverview.png");
      #endregion global tissue detection

      //part.Tiles[]
      foreach (var tile in part)
      {
        #region global tissue detection
        var rect = tile.SourceRect;
        int overviewX = rect.X / OverviewTileSize;
        int overviewY = rect.Y / OverviewTileSize;
        int windowSize = rect.Width / OverviewTileSize;

        bool tileInObject = true;
        int partsOutside = 0;

        for (int y = 0; y < windowSize; y++)
        {
          for (int x = 0; x < windowSize; x++)
          {
            int newX = overviewX + x;
            int newY = overviewY + y;

            if (newX < 0 || newX >= overviewLayer.Map.Width || newY < 0 || newY >= overviewLayer.Map.Height) { continue; }

            uint id = overviewLayer.Map[newX, newY];
            if (id != 0) continue;
            partsOutside++;
            if (!(partsOutside >= Math.Pow(windowSize + 1, 2) * 0.75)) continue;
            tileInObject = false;
            break;
          }
          if (!tileInObject) { break; }
        }
        tissueData.AddDataToCurrentTile(tileInObject);
        #endregion global tissue detection
        if (tileInObject) Console.WriteLine(tile.SourceRect + ":" + partsOutside);
      }
      tissueData.ToFile(processinHelper.DataPath + "tissueData.tpi");
      using (Bitmap b = tissueData.GenerateHeatMap(tissue => tissue ? Color.Green : Color.Red))
        b.Save(processinHelper.DataPath + "tissueData.png");

      Console.WriteLine("done");
      Console.ReadKey();
    }

    private static void DrawObjectsToImage(Bitmap bitmap, ObjectLayer layer, Color color)
    {
      //*** Exception, falls eines der Parameter null ist **********************************************
      if (bitmap == null || layer == null || color == null) { throw new NullReferenceException("Parameter ungültig!"); }

      //*** Exception, falls Bild und Objektebene unterschiedliche Dimension haben *********************
      if (bitmap.Width != layer.Map.Width || bitmap.Height != layer.Map.Height) { throw new ArgumentOutOfRangeException("bitmap"); }

      //*** Bildprozessor anlegen **********************************************************************
      using (var processor = new BitmapProcessor(bitmap))
      {
        //*** Über alle Konturpunkte der Objekte iterieren *******************************************
        foreach (Point point in layer.Objects.ToArray().SelectMany(imageObject => imageObject.Contour.GetPoints()))
        {
          //*** Konturpunkt im Bild auf gegebene Farbe setzen **************************************
          processor.SetRed(point.X, point.Y, color.R);
          processor.SetGreen(point.X, point.Y, color.G);
          processor.SetBlue(point.X, point.Y, color.B);
        }
      }
    }
  }
}
