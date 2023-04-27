﻿using Models.Interfaces;
using System;
using System.Collections.Generic;

namespace UnitTests.Surface
{

    [Serializable]
    class MockSurfaceOrganicMatter : ISurfaceOrganicMatter
    {
        public double Cover { get; set; }

        public List<ICanopy> Canopies { get; set; }

        public void Add(double biomass, double N, double P, string type, string name)
        {
            throw new NotImplementedException();
        }
    }
}
