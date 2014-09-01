using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using Glaukopis.SlideProcessing;
using SharpAccessory.Imaging.DigitalPathology;
using SharpAccessory.Imaging.Processors;
using SharpAccessory.Imaging.Segmentation;
using SharpAccessory.VirtualMicroscopy;

namespace StromaDetectionExecute
{
  class Program
  {
    private static List<Eingabe> importItems;
    private static List<Ausgabe> exportItems;

    static void Main(string[] args)
    {
      if (checkParams(args))
      {
        readInputFile(args[0]);

        processInput();

        writeResultFile(args[1]);
        Console.ReadLine();
      }
      else
      {
        Console.WriteLine("Das Programm wurde nicht korrekt aufgerufen, oder die Eingabe CSV-Datei nicht gefunden.");
        Console.WriteLine("Der korrekte Aufruf ist: ");
        Console.WriteLine("StromaDetection.exe <eingabe.csv> <ausgabe.csv>");
        Console.ReadLine();
      }
    }

    private static void processInput()
    {
      const int OverviewTileSize = 128;
      const int Radius = 2;
      const int NoiseLevel = 10;

      foreach (var import in importItems)
      {
        Console.WriteLine("Items ....");
        Console.WriteLine(import.Identify);
        Console.WriteLine(import.FileName);
        Console.WriteLine(import.UpperLeft);
        Console.WriteLine(import.LowerRight);

        var processingHelper = new Processing(import.FileName);
        var slide = processingHelper.Slide;

        var part = new WsiPartitioner(new Rectangle(new Point(0, 0), slide.Size), new Size(1000, 1000), new Size(0, 0), 1.0);
        var tissueData = new TiledProcessInformation<bool>(part, import.FileName);

        #region global tissue detection
        int overviewImageWidth = slide.Size.Width / OverviewTileSize;
        int overviewImageHeight = slide.Size.Height / OverviewTileSize;

        Bitmap overviewImage = slide.GetImagePart(0, 0, slide.Size.Width, slide.Size.Height, overviewImageWidth, overviewImageHeight);

        var bitmapProcessor = new BitmapProcessor(overviewImage);

        ObjectLayer overviewLayer = new TissueDetector().Execute(bitmapProcessor, Radius, NoiseLevel);

        bitmapProcessor.Dispose();

        DrawObjectsToImage(overviewImage, overviewLayer, Color.Black);
        overviewImage.Save(processingHelper.DataPath + "_" + import.Identify + "_" + "tissueDetectionOverview.png");
        #endregion global tissue detection
      }

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

    private static void readInputFile(string fileName)
    {
      string line;
      importItems = new List<Eingabe>();
      StreamReader file = new StreamReader(fileName);
      while ((line = file.ReadLine()) != null)
      {
        Console.WriteLine("Import Information:");
        Console.WriteLine(line);
        importItems.Add(parseLine(line));
      }
      file.Close();
    }

    private static void writeResultFile(string resultFileName)
    {
      var resultCSV = new StringBuilder();

      //für jedes Ergebnis
      string id = "0";
      string resultText = "ja";
      string errorText = "kein Fehler";
      var newCSVLine = string.Format("{0},{1},{2}", id, resultText, errorText);
      resultCSV.AppendLine(newCSVLine);

      File.WriteAllText(resultFileName, resultCSV.ToString());
    }

    private static Eingabe parseLine(string line)
    {
      string[] part = line.Split(new char[] { ';' });
      return new Eingabe
      {
        Identify = uint.Parse(part[0]),
        FileName = part[1],
        UpperLeft = new Point(int.Parse(part[2]), int.Parse(part[3])),
        LowerRight = new Point(int.Parse(part[4]), int.Parse(part[5]))
      };
    }

    private static bool checkParams(string[] parms)
    {
      bool result = false;
      if (parms.Length == 2 && File.Exists(parms[0]))
      {
        result = true;
      }
      return result;
    }

  }
}
