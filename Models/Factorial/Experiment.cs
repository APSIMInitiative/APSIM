﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Models.Core;
using Models.Factorial;

namespace Models.Factorial
{
    /// <summary>
    /// Encapsulates a factorial experiment.f
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.MemoView")]
    [PresenterName("UserInterface.Presenters.ExperimentPresenter")]
    [ValidParent(typeof(Simulations))]
    public class Experiment : Model
    {
        /// <summary>
        /// Create all simulations.
        /// </summary>
        public Simulation[] Create()
        {
            List<List<FactorValue>> allCombinations = AllCombinations();
            Simulation baseSimulation = Apsim.Child(this, typeof(Simulation)) as Simulation;

            List<Simulation> simulations = new List<Simulation>();
            foreach (List<FactorValue> combination in allCombinations)
            {
                string newSimulationName = Name;
                foreach (FactorValue value in combination)
                    newSimulationName += value.Name;

                Simulation newSimulation = Apsim.Clone(baseSimulation) as Simulation;
                newSimulation.Name = newSimulationName;
                newSimulation.Parent = null;
                Apsim.ParentAllChildren(newSimulation);

                // Call OnLoaded in all models.
                foreach (Model child in Apsim.ChildrenRecursively(newSimulation))
                    Apsim.CallEventHandler(child, "Loaded", null);

                foreach (FactorValue value in combination)
                    value.ApplyToSimulation(newSimulation);

                simulations.Add(newSimulation);
            }

            return simulations.ToArray();
        }

        /// <summary>
        /// Gets the base simulation
        /// </summary>
        public Simulation BaseSimulation
        {
            get
            {
                return Apsim.Child(this, typeof(Simulation)) as Simulation;
            }
        }

        /// <summary>
        /// Create a specific simulation.
        /// </summary>
        public Simulation CreateSpecificSimulation(string name)
        {
            List<List<FactorValue>> allCombinations = AllCombinations();
            Simulation baseSimulation = Apsim.Child(this, typeof(Simulation)) as Simulation;

            foreach (List<FactorValue> combination in allCombinations)
            {
                string newSimulationName = Name;
                foreach (FactorValue value in combination)
                    newSimulationName += value.Name;

                if (newSimulationName == name)
                {
                    Simulation newSimulation = Apsim.Clone(baseSimulation) as Simulation;
                    newSimulation.Name = newSimulationName;
                    newSimulation.Parent = null;
                    Apsim.ParentAllChildren(newSimulation);

                    // Connect events and links in our new  simulation.
                    foreach (Model child in Apsim.ChildrenRecursively(newSimulation))
                        Apsim.CallEventHandler(child, "Loaded", null);

                    foreach (FactorValue value in combination)
                        value.ApplyToSimulation(newSimulation);

                    return newSimulation;
                }
            }

            return null;
        }

        /// <summary>
        /// Return a list of simulation names.
        /// </summary>
        public string[] Names()
        {
            List<List<FactorValue>> allCombinations = AllCombinations();

            List<string> names = new List<string>();
            if (allCombinations != null)
            {
                foreach (List<FactorValue> combination in allCombinations)
                {
                    string newSimulationName = Name;

                    foreach (FactorValue value in combination)
                        newSimulationName += value.Name;

                    names.Add(newSimulationName);
                }
            }
            return names.ToArray();
        }

        /// <summary>
        /// Return a list of list of factorvalue objects for all permutations.
        /// </summary>
        private List<List<FactorValue>> AllCombinations()
        {
            Factors Factors = Apsim.Child(this, typeof(Factors)) as Factors;

            // Create a list of list of factorValues so that we can do permutations of them.
            List<List<FactorValue>> allValues = new List<List<FactorValue>>();
            if (Factors != null)
            {
                bool doFullFactorial = true;
                foreach (Factor factor in Factors.factors)
                {
                    List<FactorValue> factorValues = factor.CreateValues();
                    if (factor.Specifications.Count > 1)
                        doFullFactorial = false;
                    allValues.Add(factorValues);
                }
                if (doFullFactorial)
                    return AllCombinationsOf<FactorValue>(allValues.ToArray());
                else
                    return allValues;
            }
            return null;
        }

        /// <summary>
        /// From: http://stackoverflow.com/questions/545703/combination-of-listlistint
        /// </summary>
        private static List<List<T>> AllCombinationsOf<T>(params List<T>[] sets)
        {
            // need array bounds checking etc for production
            var combinations = new List<List<T>>();

            // prime the data
            if (sets.Length > 0)
            {
                foreach (var value in sets[0])
                    combinations.Add(new List<T> { value });

                foreach (var set in sets.Skip(1))
                    combinations = AddExtraSet(combinations, set);
            }
            return combinations;
        }

        private static List<List<T>> AddExtraSet<T>
             (List<List<T>> combinations, List<T> set)
        {
            var newCombinations = from value in set
                                  from combination in combinations
                                  select new List<T>(combination) { value };

            return newCombinations.ToList();
        }

    }

     
}
