﻿/*
The MIT License (MIT)

Copyright (c) 2016 

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Verse;
using RimWorld;
using UnityEngine;

namespace FoodAnalyticsTab
{
    public class NoteType : MapComponent
    {
        public string text = "";
        public override void ExposeData()
        {
            Scribe_Values.LookValue(ref this.text, "NotePageText");
        }
    }

    [StaticConstructorOnStartup]
    public class MainTabWindow_Estimator : MainTabWindow
    {
        //graphics 
        private enum FoodAnalyticsTab : byte
        {
            Graph= 0,
            Analytics = 1,
            Help = 2,
            DetailedList = 3,
            Note = 4
        }
        private MainTabWindow_Estimator.FoodAnalyticsTab curTab = FoodAnalyticsTab.Graph, prevTab = FoodAnalyticsTab.Analytics;
        Vector2[] scrollPos = new Vector2[3] { Vector2.zero, Vector2.zero, Vector2.zero }; // for scrolling view

        protected float lastUpdate = -1f;
        private int lastUpdateTick = 0;

        // analytics
        private float dailyHayConsumptionIndoorAnimals, dailyKibbleConsumption, dailyHayConsumption, dailyHayConsumptionRoughAnimals;
        private int numAnimals, numRoughAnimals, numKibbleAnimals, numHaygrass, numHay, numMeat, numEgg, numHen, numColonist, numHerbivoreIndoor, numHerbivoreOutdoor;
        
        private static int nextNDays = 60; // default display 25 days
        
        public static float[] debug_val = new float[10];

    
        public static Predictor predictor = new Predictor();
        List<LineChart> chartList = new List<LineChart>();

        //List<ThingDef> plantDef = new List<ThingDef>();
        private NoteType note;

        [Serializable]
        class DataPoint
        {
            string date;
            public DataPoint(string s) {
                date = s;
            }
        }
        List<DataPoint> dpList = new List<DataPoint>();
        // functions
        public MainTabWindow_Estimator () : base()
        {

            this.doCloseX = true;
            this.doCloseButton = false;
            this.closeOnClickedOutside = false;
            this.forcePause = false;
           

            predictor.predictionEnable["Haygrass"] = true;
            chartList.Add( new LineChart(nextNDays, ref predictor.predictionEnable));

            NoteType getComponent = Find.Map.components.OfType<NoteType>().FirstOrDefault();
            if (getComponent == null)
            {
                getComponent = new NoteType();
                Find.Map.components.Add(getComponent);
            }
            note = getComponent;
            //dpList.Add(new DataPoint(GenDate.DateFullStringAt(GenTicks.TicksAbs)));
            //ML.WriteToXmlFile<List<DataPoint>>("C://datapoint.xml", dpList);

        }
        public override Vector2 RequestedTabSize
        {
            get
            {
                return new Vector2(1010f, Screen.height);
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            if (Find.TickManager.Paused)
            {
                predictor.MakePrediction(0);
            }

            //dpList.Add(new DataPoint(GenDate.DateFullStringAt(GenTicks.TicksAbs)));

            //ML.WriteToBinaryFile<List<DataPoint>>("datapoint.dat", dpList);// save file under main dir
        }

        private void GetInGameData()
        {
            var pawns = Find.MapPawns.PawnsInFaction(Faction.OfPlayer);
            var allHaygrass = Find.ListerThings.AllThings.Where(x => x.Label == "haygrass");
            numColonist = pawns.Where(x => x.RaceProps.Humanlike).Count();
            numMeat = Find.ResourceCounter.GetCountIn(ThingCategoryDefOf.MeatRaw);
            numEgg = Find.ListerThings.AllThings.Where(x => x.def.defName == "EggChickenUnfertilized").Count();
            numAnimals = pawns.Where(x => x.RaceProps.Animal).Count();

            var roughAnimals = pawns
                           .Where(x => x.RaceProps.Animal && x.RaceProps.Eats(FoodTypeFlags.Plant));
            numRoughAnimals = roughAnimals.Count();
            numHerbivoreOutdoor = roughAnimals.Where(a => a.Position.GetRoomOrAdjacent().UsesOutdoorTemperature).Count();
            numHerbivoreIndoor = numRoughAnimals - numHerbivoreOutdoor;

            numHen = roughAnimals.
                Where(x => x.def.defName == "Chicken" && x.gender == Gender.Female &&
                      x.ageTracker.CurLifeStage.defName == "AnimalAdult").Count(); // x.def.defName == "Chicken" worked, but x.Label == "chicken" didn't work

            float hayNut = 1f;
            dailyHayConsumptionIndoorAnimals = roughAnimals.Where(a => a.Position.GetRoomOrAdjacent().UsesOutdoorTemperature)
                           .Sum(x => x.needs.food.FoodFallPerTick) * GenDate.TicksPerDay / hayNut;
            dailyHayConsumptionRoughAnimals = roughAnimals.Sum(x => x.needs.food.FoodFallPerTick) * GenDate.TicksPerDay / hayNut;
            //TODO: find animals' hunger rate when they are full instead whatever state they are in now.

            var kibbleAnimals = pawns.Where(x => x.RaceProps.Animal && !x.RaceProps.Eats(FoodTypeFlags.Plant) && x.RaceProps.Eats(FoodTypeFlags.Kibble));
            numKibbleAnimals = kibbleAnimals.Count();
            dailyKibbleConsumption = kibbleAnimals
                           .Sum(x => x.needs.food.FoodFallPerTick) * GenDate.TicksPerDay / hayNut;
            

            numHaygrass = allHaygrass.Count();
            numHay = Find.ListerThings.AllThings.Where(x => x.def.label == "hay").Sum(x => x.stackCount);
            
            // TODO: Find.ZoneManager.AllZones, potentially look at unplannted cells in growing zones and predict work amount and complete time.
            // look at Zone_Growing class , ZoneManager class
        }
        

        public override void DoWindowContents(Rect inRect)
        {
            base.DoWindowContents(inRect);
            if (GenTicks.TicksAbs - lastUpdateTick >= (GenDate.TicksPerHour/6)) 
            {
                lastUpdateTick = GenTicks.TicksAbs;
                predictor.MakePrediction(0);
            }


            Rect rect2 = inRect;
            rect2.yMin += 45f; // move top border downs
            List<TabRecord> list = new List<TabRecord>();
            list.Add(new TabRecord("Graph".Translate(), delegate
            {
                this.curTab = FoodAnalyticsTab.Graph;
            }, this.curTab == FoodAnalyticsTab.Graph));

            list.Add(new TabRecord("Analytics", delegate
            {
                this.curTab = FoodAnalyticsTab.Analytics;
            }, this.curTab == FoodAnalyticsTab.Analytics));
            list.Add(new TabRecord("Detailed List", delegate
            {
                this.curTab = FoodAnalyticsTab.DetailedList;
            }, this.curTab == FoodAnalyticsTab.DetailedList));
            list.Add(new TabRecord("Help", delegate
            {
                this.curTab = FoodAnalyticsTab.Help;
            }, this.curTab == FoodAnalyticsTab.Help));
            list.Add(new TabRecord("Note", delegate
            {
                this.curTab = FoodAnalyticsTab.Note;
            }, this.curTab == FoodAnalyticsTab.Note));

            TabDrawer.DrawTabs(rect2, list);

            if (this.curTab == FoodAnalyticsTab.Graph)
            {
                this.DisplayGraphPage(rect2);
            } else if (this.curTab == FoodAnalyticsTab.Analytics)
            {
                this.DisplayAnalyticsPage(rect2);
            } else if (this.curTab == FoodAnalyticsTab.Help)
            {
                this.DisplayHelpPage(rect2);
            } else if (this.curTab == FoodAnalyticsTab.DetailedList)
            {
                this.DisplayDetailedListPage(rect2);
            } else if (this.curTab == FoodAnalyticsTab.Note)
            {
                this.DisplayNotePage(rect2);
            }
        }

        

        private void DisplayNotePage(Rect rect)
        {
            rect.y += 6;
            note.text = GUI.TextArea(rect, note.text);
        }
        private void DisplayAnalyticsPage(Rect rect)
        {
            // constructing string
            string analysis = "Number of animals you have = " + (int)numAnimals + ", hay animals = " + numRoughAnimals + ", kibble animals = " + numKibbleAnimals +
                            "\nNumber of colonist = " + numColonist +
                            "\nNumber of hay in stockpiles and on the floors = " + (int)numHay + ", number of meat = " + numMeat + ", number of egg = " + numEgg +
                            "\nNumber of hen = " + numHen +
                            "\nEstimated number of hay needed daily for hay-eaters only= " + (int)dailyHayConsumptionIndoorAnimals +
                            "\nEstimated number of kibble needed for all kibble-eaters = " + (int)dailyKibbleConsumption + ", meat =" + dailyKibbleConsumption*2/5 + ", egg=" + dailyKibbleConsumption*4/50 +
                            "\nEstimated number of hay needed daily for all animals = " + (int)dailyHayConsumption +
                            "\nNumber of days until hay in stockpiles run out = " + String.Format("{0:0.0}", numHay / dailyHayConsumption) +
                            "\nDays Until Winter = " + Predictor.daysUntilWinter + ", Days until growing period over = " + Predictor.daysUntilGrowingPeriodOver +
                            ", Days until the end of winter = " + Predictor.daysUntilEndofWinter + ", Days until next harvest season = " + Predictor.daysUntilNextHarvestSeason +
                            "\nEstimated number of hay needed until winter for hay-eaters only = " + (int)(dailyHayConsumptionIndoorAnimals * Predictor.daysUntilWinter) +
                            "\nEstimated number of hay needed until winter for all animals = " + (int)(dailyHayConsumption * Predictor.daysUntilWinter) +
                            "\nEstimated number of hay needed until the end of winter for hay-eaters only = " + (int)(dailyHayConsumptionIndoorAnimals * Predictor.daysUntilEndofWinter) +
                            "\nEstimated number of hay needed until the end of winter for all animals = " + (int)(dailyHayConsumption * Predictor.daysUntilEndofWinter) +
                            "\nEstimated number of hay needed yearly for hay-eaters only = " + (int)(dailyHayConsumptionIndoorAnimals * GenDate.DaysPerMonth * GenDate.MonthsPerYear) + // 60 days
                            "\nEstimated number of hay needed yearly for all animals = " + (int)(dailyHayConsumption * GenDate.DaysPerMonth * GenDate.MonthsPerYear) + // 60 days
                            "\nEstimated number of hay needed until next harvest season(10th of Spring) for all animals = " + (int)(dailyHayConsumption * Predictor.daysUntilNextHarvestSeason) +
                            "\nNumber of haygrass plant = " + (int)numHaygrass + ", outdoor = " +// allHaygrassGrowth.Where(h => h.IsOutdoor).Count() +
                            "\nNumber of haygrass needed = " + dailyHayConsumption / 20 * 10 +
                            "\nEstimate of projected harvest production:\n";
            analysis += "Day\t Max Yield Min Yield Max Stock Min Stock\n";
            int i = 0;
            /*
            foreach (Prediction k in projectedRecords)
            {
                analysis += String.Format("{0,-2}\t {1,-6}\t {2,-6}\t {3,-6}\t {4,-6}\n",
                    i, (int)k.hay_yield.max, (int)k.hay_yield.min, (int)k.hay_stock.max, (int)k.hay_stock.min);
                i++;
            }
            */
            
            foreach (var v in debug_val)
            {
                analysis += v + ",";
            }
            foreach (string s in predictor.predictionEnable.Keys)
            {
                analysis += s + ",";
            }
            /*
            foreach (ThingDef x in plantDef)
            {
                analysis += x.defName + ",";
            }
            
            analysis += ",\n" + GenDate.CurrentMonth + "," + GenDate.CurrentSeason + "," +
                                        GenDate.DayOfMonth + "," + GenDate.DayOfYear + "," +
                                        GenTicks.TicksAbs + "," + Find.TickManager.TicksAbs + ",";
            //*/

            // draw text
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Rect rect2 = new Rect(0, 0, rect.width, analysis.Split('\n').Length * 20);
            Widgets.BeginScrollView(rect, ref this.scrollPos[(int)FoodAnalyticsTab.Analytics], rect2);
            Widgets.Label(rect2, analysis);
            Widgets.EndScrollView();
        }

        private void DisplayGraphPage(Rect rect)
        {
            Rect btn = new Rect(0, rect.yMin + 6, 110f, 40f);
            if (Widgets.ButtonText(btn, "New Chart", true, false, true))
            {
                chartList.Add(new LineChart(60, ref predictor.predictionEnable));
            }

            
            if (!chartList.NullOrEmpty())
            {
                // update curves in each chart
                foreach (LineChart c in chartList)
                {
                    c.UpdateData(ref predictor);
                }

                // start drawing all charts
                rect.yMin = btn.yMax;
                Widgets.BeginScrollView(rect, ref this.scrollPos[(int)FoodAnalyticsTab.Graph],
                    new Rect(rect.x, rect.yMin, chartList[0].rect.width, chartList[0].rect.height * chartList.Count())); 
                                                                                                                      
                Rect newRect = new Rect(rect.xMin, btn.yMax, rect.width, rect.height);
                foreach (LineChart g in chartList)
                {
                    g.Draw(newRect); 
                    if (g.remove == true)
                    {
                        newRect = new Rect(g.rect.x, g.rect.yMin, rect.width, rect.height);
                    }
                    else
                    {
                        newRect = new Rect(g.rect.x, g.rect.yMax, rect.width, rect.height);
                    }
                }
                chartList.RemoveAll(g => g.remove == true);

                predictor.EnablePrediction(chartList);

                Widgets.EndScrollView();
            }
        }

        private void DisplayHelpPage(Rect rect)
        {
            
        }

        public enum SourceOptions
        {
            Plants,
            Animals
        }
        public SourceOptions Source = SourceOptions.Animals;
        public static bool IsDirty;
        private void DisplayDetailedListPage(Rect rect)
        {
            GUI.BeginGroup(rect);
            var x = 0;
            var sourceButton = new Rect(0f, 6f, 200f, 35f);
            if (Widgets.ButtonText(sourceButton, Source.ToString().Translate()))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                if (Source != SourceOptions.Plants)
                    options.Add(new FloatMenuOption("Plants", delegate {
                        Source = SourceOptions.Plants;
                        IsDirty = true;
                    }));

                if (Source != SourceOptions.Animals)
                    options.Add(new FloatMenuOption("Animals", delegate {
                        Source = SourceOptions.Animals;
                        IsDirty = true;
                    }));

                Find.WindowStack.Add(new FloatMenu(options));
            }
            var offset = true;
            List<PawnKindDef> pawnTypeList = DefDatabase<PawnKindDef>.AllDefs.Where(a => a.RaceProps.Animal).ToList();

            var nameLabel = new Rect(x, sourceButton.height + 50f, 175f, 30f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(nameLabel, "Name");
            
            TooltipHandler.TipRegion(nameLabel, "ClickToSortByName");
            Widgets.DrawHighlightIfMouseover(nameLabel);
            x += 175;
            // extra 15f offset for... what? makes labels roughly align.
            List<String> headerNames = new List<String>(){"Total","Adult","Teen","Baby", "Daily Consumption[nut]", "Daily Consumption[hay]"};
            var colWidth = (rect.width - x) / headerNames.Count;
            for (var i = 0; i < headerNames.Count; i++)
            {
                var labelRect = new Rect(x + colWidth * i - colWidth / 2, sourceButton.height + 10 + (offset ? 10f : 40f), colWidth * 2,
                                         30f);
                Widgets.DrawLine(new Vector2(x + colWidth * (i + 1) - colWidth / 2,  sourceButton.height + 40f + (offset ? 5f : 35f)),
                                  new Vector2(x + colWidth * (i + 1) - colWidth / 2,  sourceButton.height + 80f), Color.gray, 1);
                Widgets.Label(labelRect, headerNames[i]);
                /*
                if (Widgets.ButtonInvisible(defLabel))
                {
                    if (OrderBy == Order.Efficiency && OrderByCapDef == CapDefs[i])
                    {
                        Asc = !Asc;
                    }
                    else
                    {
                        OrderBy = Order.Efficiency;
                        OrderByCapDef = CapDefs[i];
                        Asc = true;
                    }
                    IsDirty = true;
                }
                //*/
                TooltipHandler.TipRegion(labelRect, "ClickToSortBy" + (headerNames[i]));
                Widgets.DrawHighlightIfMouseover(labelRect);

                offset = !offset;
            }

            GUI.EndGroup();
        }
    }
}