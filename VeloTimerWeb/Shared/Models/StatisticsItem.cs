﻿using Slugify;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VeloTimer.Shared.Models
{
    public class StatisticsItem
    {
        public long Id { get; set; }

        public string Label { get; set; }
        public double Distance { get; set; }
        public bool IsLapCounter { get; set; } = false;
        public string Slug { get; set; }

        public StatisticsItemWeb ToWeb()
        {
            var web = new StatisticsItemWeb
            {
                Label = Label,
                Slug = Slug,
                Distance = Distance,
                IsLapCounter = IsLapCounter
            };

            return web;
        }

        public static StatisticsItem Create(string label, double distance, bool isLapCounter)
        {
            var slugHelper = new SlugHelper();

            StatisticsItem item = new StatisticsItem
            {
                Label = label,
                Distance = distance,
                IsLapCounter = isLapCounter,
                Slug = slugHelper.GenerateSlug(label)
            };

            return item;
        }
    }
}
