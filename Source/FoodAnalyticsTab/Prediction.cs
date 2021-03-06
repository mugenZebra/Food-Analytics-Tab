﻿using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;
using System.Globalization;

namespace FoodAnalyticsTab
{
    /* prediction class:
       making prediction 
       types: crop/population/meat yield, vegie/meat/meal/drug/leather/population stock, 

       type of update: 1st day, after
       data structure
       list of crops
         yield
         stock
         consumption

       model:
       1.
       chicken -> egg--↘
       animals -> meat -> kibble -> carnivores animal
       merchant->       ^ meals -> conlonists
       hunting ->       | training -> animals
                        |           ^
                        |__cooking  |__feeding
       2.
       growers -> haygrass -> hay -> herbivores
                ^           ^     ↘ kibble
                |           |
                |__planting |__harvesting
       3.
       grower -> rice plant -> rice -------------> meals -> colonist
              -> corn plant -> corn                      |
              -> potato plant -> potato                  |
              -> strawberry plant -> strawberry ----------
       4.
       cotton
       muffalo     -> wool -> armchair, 
       alpaca              -> clothing
       Megatherium         -> medicine
       Dromedary

       5.
       fertilized eggs -> chicken babies -> meat -> carnivores
                                          ^
                                          |_butchering

        data structure:
        Predictor contain all prediction items, eg, prediction for hay, population
        Prediction item include prediction terms, eg, stock, yield, consumption
        Q.what should contain the 60-day prediction? Prediction obj or PredTerm?
        I think PredTerm is for daily prediction result
    */
    public class Predictor
    {
        public enum ModelType
        {
            analytical, iterative, learning
        }

        // important dates
        public static int daysUntilWinter, // to Dec 1st
                          daysUntilEndofWinter, // to February 5th
                          daysUntilGrowingPeriodOver, // to 10th of Fall, Oct 5th
                          daysUntilNextHarvestSeason, // to 10th of Spring, April 5th
                          numTicksBeforeResting = 0,
                          nextNDays = 60;

        private class GrowthTracker
        {
            public GrowthTracker(float a, float b)
            {
                Growth = a;
                GrowthPerTick = b;
            }
            public float Growth { get; set; }
            public float GrowthPerTick { get; set; }
            public bool IsOutdoor { get; set; }
        };
        public class PredType // should contain update rule
        {

            public class MinMax
            {
                public bool showDeficiency { get; set; } = false;
                private int _min = 0, _max = 0;
                public int min
                {
                    get { return _min; }
                    set
                    {
                        _min = value;
                        if (showDeficiency != true && value < 0)
                        {
                            _min = 0;
                        }
                    }
                }
                public int max
                {
                    get { return _max; }
                    set
                    {
                        _max = value;
                        if (showDeficiency != true && value < 0)
                        {
                            _max = 0;
                        }
                    }
                }

                public MinMax()
                {

                }
                public MinMax(int max, int min)
                {
                    _max = max;
                    _min = min;
                }
                public static implicit operator MinMax(int val)
                {
                    return new MinMax(val, val);
                }
            }
            public class DayPred
            {
                public int day = 0;
                public MinMax yield = new MinMax(), consumption = new MinMax(), stock = new MinMax();

                public DayPred(int day)
                {
                    this.day = day;
                }
            }

            public ThingDef def;
            public bool enabled { get; set; }
            private List<GrowthTracker> allGrowth = new List<GrowthTracker>();
            public List<DayPred> projectedPred = new List<DayPred>();

            private bool _showDeficiency;
            public bool showDeficiency
            {
                get { return _showDeficiency; }
                set
                {
                    _showDeficiency = value;
                }
            }

            public string analysis { private set; get; }

            public PredType(ThingDef def)
            {
                showDeficiency = false;
                enabled = false;
                this.def = def;
                analysis = "";
            }

            public int consumption0 , consumption = 0;
            public void SetUpdateRule(int v0, int v)
            {
                consumption0 = v0;
                consumption = v;
            }
            public void GetCurrentStat()
            {
                allGrowth.Clear();
                // get current growth stats
                foreach (Thing h in Find.ListerThings.AllThings.Where(x => x.def.defName == this.def.defName)) 
                {
                    allGrowth.Add(new GrowthTracker(((Plant)h).Growth,
                        ((Plant)h).GrowthRate / (GenDate.TicksPerDay * h.def.plant.growDays)));
                    allGrowth.Last().IsOutdoor = h.Position.GetRoomOrAdjacent().UsesOutdoorTemperature;
                }

                if (this.def.plant.harvestedThingDef != null)
                {
                    // get current harvest stock
                    projectedPred.Clear();
                    projectedPred.Add(new DayPred(0));
                    projectedPred.Last().stock.max = projectedPred.Last().stock.min =
                        Find.ListerThings.AllThings.Where(x => x.def.defName == this.def.plant.harvestedThingDef.defName).Sum(x => x.stackCount);
                    projectedPred.Last().consumption.max = projectedPred.Last().consumption.min = consumption0;
                }
            }

            public void UpdatePrediction()
            {
                if (enabled)
                {
                    //*
                    // calculate yield for today                   
                    foreach (GrowthTracker g in allGrowth)
                    {
                        g.Growth += Predictor.numTicksBeforeResting * g.GrowthPerTick;

                        if (g.Growth >= 1.0f)
                        {
                            g.Growth = Plant.BaseGrowthPercent;
                            projectedPred[0].yield.max = (int) (this.def.plant.harvestYield * Find.Storyteller.difficulty.cropYieldFactor);
                            projectedPred[0].yield.min = (int) (this.def.plant.harvestYield * 0.5f * .5f * Find.Storyteller.difficulty.cropYieldFactor);
                        }
                    }
                    //*
                    if (this.def.plant.harvestedThingDef.defName == "Hay")
                    {
                        projectedPred[0].stock.max -= projectedPred[0].consumption.max;
                        projectedPred[0].stock.min -= projectedPred[0].consumption.min;
                    }
                    //*/

                    projectedPred[0].stock.max += projectedPred[0].yield.max;
                    projectedPred[0].stock.min += projectedPred[0].yield.min;

                    /*
                    projectedRecords[0].meat_stock.max -= (1 - GenDate.CurrentDayPercent) * dailyKibbleConsumption * 2f / 5f; // convert every 50 kibbles to 20 meat
                    projectedRecords[0].meat_stock.min -= (1 - GenDate.CurrentDayPercent) * dailyKibbleConsumption * 2f / 5f;
                    */

                    // calculate yields and stocks after today
                    for (int day = 1; day < nextNDays; day++)
                    {
                        projectedPred.Add(new DayPred(day));

                        foreach (GrowthTracker g in allGrowth)
                        {
                            if (g.IsOutdoor && !(day <= Predictor.daysUntilGrowingPeriodOver))
                            {
                                continue; // don't count outdoor crop if it's growing period is over
                            }
                            g.Growth += g.GrowthPerTick * GenDate.TicksPerDay * 0.55f; // 0.55 is 55% of time plant spent growing

                            if (g.Growth >= 1.0f)
                            {
                                projectedPred[day].yield.max += (int)this.def.plant.harvestYield;
                                projectedPred[day].yield.min += (int)(this.def.plant.harvestYield * 0.5f * 0.5f);
                                g.Growth = Plant.BaseGrowthPercent; // if it's fully grown, replant and their growths start at 5%.
                            }
                        }

                        if (this.def.plant.harvestedThingDef.defName == "Hay")
                        {
                            projectedPred[day].stock.max = projectedPred[day - 1].stock.max + projectedPred[day].yield.max - consumption;// projectedPred[1].consumption.max;
                            projectedPred[day].stock.min = projectedPred[day - 1].stock.min + projectedPred[day].yield.min - consumption;// projectedPred[1].consumption.min;
                        }
                        /*
                        projectedRecords[day].meat_stock.max -= dailyKibbleConsumption * 2f / 5f;
                        projectedRecords[day].meat_stock.min -= dailyKibbleConsumption * 2f / 5f;
                        */
                    }
                    //*/
                }
            }

            public void GenerateAnalysis()
            {
                this.analysis = 
                            "\n\n" + this.def.defName + " Specific Stats:" +
                            //"\nEstimated number of hay needed daily for hay-eaters only= " + (int)dailyHayConsumptionIndoorAnimals +
                            "\nNumber of haygrass planted = " + (int)allGrowth.Count() + ", outdoor = " + allGrowth.Where(h => h.IsOutdoor).Count() +
                            "\nEstimated haygrass needed daily = " + consumption / 20 * 10 + // /20 is yield per haygrass * 10 = 10 days growth
                            "\nNumber of " + this.def.plant.harvestedThingDef.defName + " in stockpiles and on the floor " + projectedPred[0].stock.max +
                            "\nNumber of days until hay in stockpiles run out = " + String.Format("{0:0.0}", (float) projectedPred[0].stock.max / (float) consumption) +
                            "\nEstimated hay needed daily for all animals = " + (int)consumption +
                            //"\nEstimated hay needed until winter for hay-eaters only = " + (int)(dailyHayConsumptionIndoorAnimals * Predictor.daysUntilWinter) +
                            "\nEstimated hay needed until winter for all animals = " + (int)(consumption * Predictor.daysUntilWinter) +
                            //"\nEstimated hay needed until the end of winter for hay-eaters only = " + (int)(dailyHayConsumptionIndoorAnimals * Predictor.daysUntilEndofWinter) +
                            "\nEstimated hay needed until the end of winter for all animals = " + (int)(consumption * Predictor.daysUntilEndofWinter) +
                            //"\nEstimated hay needed yearly for hay-eaters only = " + (int)(dailyHayConsumptionIndoorAnimals * GenDate.DaysPerMonth * GenDate.MonthsPerYear) + // 60 days
                            "\nEstimated hay needed yearly for all animals = " + (int)(consumption * GenDate.DaysPerMonth * GenDate.MonthsPerYear) + // 60 days
                            "\nEstimated hay needed until next harvest season(10th of Spring) for all animals = " + (int)(consumption * Predictor.daysUntilNextHarvestSeason) +
                            "\n\nProjected " + this.def.plant.harvestedThingDef.defName + " production:\n";
                
                
                analysis += "Day\t Max Yield Min Yield Max Stock Min Stock\n";
                for (int i = 0; i < projectedPred.Count(); i++) 
                {
                    analysis += String.Format("{0,-2}\t {1,-6}\t {2,-6}\t {3,-6}\t {4,-6}\n",
                        i, (int)projectedPred[i].yield.max, (int)projectedPred[i].yield.min, (int)projectedPred[i].stock.max, (int)projectedPred[i].stock.min);
                }
            }
        }
        public Dictionary<string, PredType> allPredType = new Dictionary<string, PredType>();
       
        private List<ThingDef> plantDefs = new List<ThingDef>();

        public Predictor()
        {
            plantDefs = DefDatabase<ThingDef>.AllDefs.Where(x => x.plant != null && x.plant.Sowable && x.plant.harvestedThingDef != null).ToList();
            foreach (ThingDef x in plantDefs.OrderBy(x => x.label))
            {
                allPredType.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(x.label.ToLower()), new PredType(x));
            }
        }

        public void MakePrediction(int days)
        {
            // exclude resting plants' growth
            if (GenDate.CurrentDayPercent < 0.25f) // if resting before 6am
            {
                numTicksBeforeResting = (int) (GenDate.TicksPerDay * 0.55f); // will grow the full day
            }
            else if (GenDate.CurrentDayPercent > 0.8f) // if resting after 7.2pm
            {
                numTicksBeforeResting = 0; // won't grow anymore
            }
            else // from .25 to .8 
            {
                numTicksBeforeResting = (int) (GenDate.TicksPerDay * (0.8f - GenDate.CurrentDayPercent));
            }
            UpdateDates();
            foreach (String s in this.allPredType.Keys)
            {
                if (allPredType[s].enabled)
                {
                    allPredType[s].GetCurrentStat();
                    allPredType[s].UpdatePrediction();
                }
            }
        }

        public void EnablePrediction(List<LineChart> chartList)
        {
            foreach (string s in allPredType.Keys)
            {
                allPredType[s].enabled = false;
                
                if (chartList.Where(c => c.setting.graphEnable[s] == true).Any())
                {
                    allPredType[s].enabled = true;
                }
            }
        }

        // calculating number of days until certain dates
        private void UpdateDates()
        {

            Predictor.daysUntilWinter = ((Month.Dec - GenDate.CurrentMonth - 1) * GenDate.DaysPerMonth + (GenDate.DaysPerMonth - GenDate.DayOfMonth)); // to Dec 1st

            if (GenDate.CurrentMonth > Month.Feb)
            {
                Predictor.daysUntilEndofWinter = ((13 - (int)GenDate.CurrentMonth) * GenDate.DaysPerMonth + (GenDate.DaysPerMonth - GenDate.DayOfMonth));
            }
            else
            {
                Predictor.daysUntilEndofWinter = ((Month.Feb - GenDate.CurrentMonth) * GenDate.DaysPerMonth + (GenDate.DaysPerMonth - GenDate.DayOfMonth));
            }

            if (GenDate.CurrentMonth >= Month.Mar && GenDate.CurrentMonth < Month.Nov)
            {
                Predictor.daysUntilGrowingPeriodOver = ((Month.Oct - GenDate.CurrentMonth) * GenDate.DaysPerMonth + (GenDate.DaysPerMonth - GenDate.DayOfMonth));
            }
            else
            {
                Predictor.daysUntilGrowingPeriodOver = 0;
            }

            if (GenDate.CurrentMonth > Month.Apr && GenDate.CurrentMonth <= Month.Dec)
            {
                Predictor.daysUntilNextHarvestSeason = ((15 - (int)GenDate.CurrentMonth) * GenDate.DaysPerMonth + (GenDate.DaysPerMonth - GenDate.DayOfMonth));
            }
            else
            {
                Predictor.daysUntilNextHarvestSeason = ((Month.Apr - GenDate.CurrentMonth) * GenDate.DaysPerMonth + (GenDate.DaysPerMonth - GenDate.DayOfMonth));
            }
        }
    }
}
