﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;
using UnityEngine;
using System.Globalization;

namespace FoodAnalyticsTab
{
    /* settings for each graph:
       what data to show
         projected yield, stock, population
         work, time

       TODO:show deficiency, it turns out that if the current Predictor class design use MinMax object to show deficiency, then deficiency will be shown to all graph
       unless it's computed every time for each graph which is not optimal, or two versions can be kept, which is also not optimal

       internal:
         SimpleCurveDrawerStyle
         are settings changed        
    */

    public class ChartSettings : MapComponent
    {
        private string title = "";
        public Dictionary<String, bool> graphEnable = new Dictionary<String, bool>();
        public bool ShowDeficiency, DrawPoints, UseAntiAliasedLines, EnableLearning, EnableOutdoorAnimalDetection,
            EnableOutdoorNoGrowWinter;
        public Predictor.ModelType predictorModel = Predictor.ModelType.iterative;

        public ChartSettings()
        {
            SetDefault();
        }

        public override void ExposeData()
        {
            Scribe_Values.LookValue(ref this.title, "ChartTitle" + this.title);
        }

        public void SetDefault()
        {
            ShowDeficiency = false;
            DrawPoints = false;
            UseAntiAliasedLines = true;
            predictorModel = Predictor.ModelType.iterative;
            EnableLearning = false;
            EnableOutdoorAnimalDetection = true;
            EnableOutdoorNoGrowWinter = true;
        }
    }

    public class LineChart
    {
        private List<CurveMark> marks = new List<CurveMark>();
        private Dictionary<String, SimpleCurveDrawInfo> curves = new Dictionary<String, SimpleCurveDrawInfo>();
        private SimpleCurveDrawerStyle curveDrawerStyle = new SimpleCurveDrawerStyle();

        private float scrollPos_curr;
        private float scrollPos_prev;
        public Rect rect { get; private set; } // region defines this LineGraph
        public bool changed = false;
        static int min_day = 1, max_day = 60;
        public bool remove = false;
        public ChartSettings setting = new ChartSettings();

        public LineChart(float default_day, ref Predictor p)
        {
            this.scrollPos_curr = this.scrollPos_prev = default_day;
            setting.graphEnable = p.allPredType.ToDictionary(k => k.Key, k => false);
            setting.SetDefault();
            SetDefaultStyle();
        }
        public LineChart(LineChart lg)
        {
            this.scrollPos_curr = this.scrollPos_prev = lg.scrollPos_curr;
            SetDefaultStyle();
            this.marks = new List<CurveMark>(lg.marks);
            this.curves = new Dictionary<String, SimpleCurveDrawInfo>(lg.curves);
            this.setting.graphEnable = lg.setting.graphEnable;
        }
        private void SetDefaultStyle()
        {
            curveDrawerStyle.UseFixedSection = true;
            curveDrawerStyle.FixedSection = new Vector2(0, scrollPos_curr);
            curveDrawerStyle.LabelY = "#";
            curveDrawerStyle.LabelX = "Day";
            curveDrawerStyle.UseFixedScale = false; // TODO: hopefully can figure out how to have y axis adjust automatically when x axis max changes
            curveDrawerStyle.DrawBackground = true; // draw gray background behind graph
            curveDrawerStyle.DrawBackgroundLines = true; // 
            curveDrawerStyle.DrawMeasures = true;
            curveDrawerStyle.MeasureLabelsXCount = (int)this.scrollPos_curr; // number of marks on x axis 
            curveDrawerStyle.MeasureLabelsYCount = 5;
            curveDrawerStyle.DrawPoints = false; // draw white points for each data
            curveDrawerStyle.DrawLegend = true; //
            curveDrawerStyle.DrawCurveMousePoint = true; // hover over graph shows details
            curveDrawerStyle.UseAntiAliasedLines = true; // smooth lines

        }

        public void SetMarks(float x, string message, Color color)
        {
            if (!this.marks.Where(s => s.message == message).Any())
            {
                this.marks.Add(new CurveMark(x, message, color));
                this.marks.Add(new CurveMark(x, message, color)); // fix i++ bug.
            }
            else
            {
                foreach (CurveMark m in this.marks.Where(s => s.message == message))
                {
                    m.x = x;
                }
            }
        }
        public void SetCurve(String label, Color color, List<float> points)
        {
            //TODO: fix labels of legends issue of fixed text width
            //TODO: expand rect of legend accordingly
            if (!this.curves.ContainsKey(label))
            {
                this.curves.Add(label, new SimpleCurveDrawInfo());
                this.curves[label].color = color;
                this.curves[label].label = label;
            }
            this.curves[label].curve = new SimpleCurve();
            for (int day = 0; day < points.Count(); day++)
            {
                this.curves[label].curve.Add(new CurvePoint(day, points[day]));
            }
        }
        public void RemoveCurve(String label)
        {
            if (this.curves.ContainsKey(label))
            {
                this.curves.Remove(label);
            }
        }
        public void Draw(Rect rect)
        {
            curveDrawerStyle.FixedSection = new Vector2(0, this.scrollPos_curr);
            curveDrawerStyle.MeasureLabelsXCount = (int)this.scrollPos_curr; // number of marks on x axis 

            Rect graphRect = new Rect(rect.x, rect.y, rect.width * .9f, 450f);
            Rect legendRect = new Rect(rect.x, graphRect.yMax, graphRect.width, 40f);
            Rect sliderRect = new Rect(rect.x, legendRect.yMax, graphRect.width, 50f);

            SimpleCurveDrawer.DrawCurves(graphRect, this.curves.Values.ToList(), this.curveDrawerStyle, this.marks, legendRect);
            this.scrollPos_prev = this.scrollPos_curr;
            this.scrollPos_curr = Widgets.HorizontalSlider(sliderRect, this.scrollPos_curr, min_day, max_day);

            this.rect = new Rect(graphRect.x, graphRect.y, graphRect.width, graphRect.height + legendRect.height + sliderRect.height);

            Rect deleteBtn = new Rect(graphRect.xMax + 6, graphRect.yMin, (rect.width - graphRect.width)/1.5f, 40f);
            if (Widgets.ButtonText(deleteBtn, "Delete".Translate(), true, true, true))
            {
                this.remove = true;
            }
            if (Widgets.ButtonText(new Rect(deleteBtn.x, deleteBtn.yMax, deleteBtn.width, deleteBtn.height),"Setting", true, true, true))
            {
                Find.WindowStack.Add(new Dialog_LineChartConfig(ref this.setting));
            }
            UpdateSetting();
        }
        private void UpdateSetting()
        {
            curveDrawerStyle.DrawPoints = this.setting.DrawPoints;
            curveDrawerStyle.UseAntiAliasedLines = this.setting.UseAntiAliasedLines;
        }
        public void UpdateData(ref Predictor predictor)
        {

            //marks add dots on top of a graph, the text label is the text in the popup box
            this.SetMarks(Predictor.daysUntilNextHarvestSeason, "Days until the Next Harvest Season", Color.green);
            this.SetMarks(Predictor.daysUntilGrowingPeriodOver, "Days until Growing Period is Over", Color.red);
            this.SetMarks(Predictor.daysUntilWinter, "Days until the Winter", Color.white);
            this.SetMarks(Predictor.daysUntilEndofWinter, "Days until the End of Winter", Color.yellow);

            foreach (String s in this.setting.graphEnable.Where(x => x.Value == true).Select(x => x.Key))
            {
                Color c1, c2;
                GenerateRandomColorPair(this.setting.graphEnable.Keys.ToList().FindIndex(x => x == s), out c1, out c2);
                if (predictor.allPredType[s].def.plant.harvestedThingDef.label == "wood")
                {
                    this.SetCurve(s + " Wood Yield(Max)", c1, predictor.allPredType[s].projectedPred.Select(x => (float)x.yield.max).ToList());
                    this.SetCurve(s + " Wood Yield(Min)", c2, predictor.allPredType[s].projectedPred.Select(x => (float)x.yield.min).ToList());
                } else if (predictor.allPredType[s].def.plant.harvestedThingDef.defName == "Hay") {
                    this.SetCurve("Hay Yield(Max)",
                    c1, predictor.allPredType[s].projectedPred.Select(x => (float)x.yield.max).ToList());
                    this.SetCurve("Hay Yield(Min)",
                        c2, predictor.allPredType[s].projectedPred.Select(x => (float)x.yield.min).ToList());
                    this.SetCurve("Hay Stock(Max)", Color.white, predictor.allPredType[s].projectedPred.Select(x => (float)x.stock.max).ToList());
                    this.SetCurve("Hay Stock(Min)", Color.black, predictor.allPredType[s].projectedPred.Select(x => (float)x.stock.min).ToList());
                }
                else
                {
                    this.SetCurve(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(predictor.allPredType[s].def.plant.harvestedThingDef.label) + " Yield(Max)",
                    c1, predictor.allPredType[s].projectedPred.Select(x => (float)x.yield.max).ToList());
                    this.SetCurve(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(predictor.allPredType[s].def.plant.harvestedThingDef.label) + " Yield(Min)",
                        c2, predictor.allPredType[s].projectedPred.Select(x => (float)x.yield.min).ToList());
                }
            }

            foreach (String s in this.setting.graphEnable.Where(x => x.Value == false).Select(x => x.Key))
            {
                if (predictor.allPredType[s].def.plant.harvestedThingDef.label == "wood")
                {
                    this.RemoveCurve(s + " Wood Yield(Max)");
                    this.RemoveCurve(s + " Wood Yield(Min)");
                }
                else if (predictor.allPredType[s].def.plant.harvestedThingDef.defName == "Hay")
                {
                    this.RemoveCurve("Hay Yield(Max)");
                    this.RemoveCurve("Hay Yield(Min)");
                    this.RemoveCurve("Hay Stock(Max)");
                    this.RemoveCurve("Hay Stock(Min)");
                }
                else
                {
                    this.RemoveCurve(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(predictor.allPredType[s].def.plant.harvestedThingDef.label) + " Yield(Max)");
                    this.RemoveCurve(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(predictor.allPredType[s].def.plant.harvestedThingDef.label) + " Yield(Min)");
                }
            }
        }
        
        private void GenerateRandomColorPair(int i, out Color c1, out Color c2)
        {
            float h =  (float) i / setting.graphEnable.Count();
            c1 = Color.HSVToRGB(h, 0.7f, 0.75f);
            c2 = Color.HSVToRGB(h, 0.3f, 0.75f);
        }

    }

}
