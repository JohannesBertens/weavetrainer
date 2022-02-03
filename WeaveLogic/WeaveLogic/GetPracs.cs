using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;

namespace WeaveLogic
{

    // Weave info (including difficulty) and most of the calculations taken from http://wotmudarchives.org/tools/wtrainerscript.js - THANK YOU THUVIA!
    public static class GetPracs
    {

        [FunctionName("GetPracs")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            if (!int.TryParse(req.Query["int"], out int intelligence) || intelligence <= 0)
            {
                return new BadRequestObjectResult("Error on 'int' parameter, needs to be greater than 0.");
            }

            if (!int.TryParse(req.Query["rank"], out int rank) || rank < 0)
            {
                return new BadRequestObjectResult("Error on 'rank' parameter, needs to be greater than or equal to 0.");
            }

            // Saving input in parameters
            var parameters = req.Query.Keys.Cast<string>().ToDictionary(k => k, v => req.Query[v].ToString());
            parameters.Remove("int");
            parameters.Remove("rank");
            log.LogInformation(string.Join(Environment.NewLine, parameters));

            var elementalReqs = new ElementalRequirements();
            foreach (var weave in parameters)
            {
                if (!WeaveInfo.ContainsKey(weave.Key))
                {
                    return new BadRequestObjectResult($"Weave {weave.Key} not known.");
                }

                elementalReqs.UpdateReqs(WeaveInfo[weave.Key]);
            }

            var praccedWeaves = new List<Weave>();
            foreach (var key in parameters.Keys)
            {
                if (!int.TryParse(parameters[key], out int percentage) || percentage <= 0 || percentage >= 100)
                {
                    return new BadRequestObjectResult($"Error on {key} parameter, percentage needs to be a number between 0 and 100, is {percentage}");
                }

                praccedWeaves.Add(new Weave(key, intelligence, rank, percentage));
            }

            // OK, weaves set. Now to do some voodoo.
            List<Weave> orderedWeaves = GetWeaveOrder(elementalReqs, praccedWeaves);

            var returnObject = new ReturnObject() { weavesDebug = praccedWeaves };
            returnObject.intelligence = intelligence;
            returnObject.rank = rank;
            returnObject.elements.AddRange(elementalReqs.GetElementalStringList());
            returnObject.elementPracs = elementalReqs.GetElementalPracs();
            foreach (var weave in praccedWeaves) returnObject.weavePracs += weave.pracsNeeded;
            returnObject.totalPracs = returnObject.elementPracs + returnObject.weavePracs;
            returnObject.levelRequired = GetLevelRequired(returnObject.totalPracs);
            returnObject.commands.AddRange(elementalReqs.GetElementalCommands());
            //foreach (var weave in praccedWeaves) returnObject.commands.AddRange(weave.GetCommands());

            return new OkObjectResult(JsonConvert.SerializeObject(returnObject));
        }

        private static void RotateRight(List<Weave> sequence, int count)
        {
            Weave tmp = sequence[count - 1];
            sequence.RemoveAt(count - 1);
            sequence.Insert(0, tmp);
        }

        private static IEnumerable<List<Weave>> Permutate(List<Weave> sequence, int count)
        {
            if (count == 1) yield return sequence;
            else
            {
                for (int i = 0; i < count; i++)
                {
                    foreach (var perm in Permutate(sequence, count - 1))
                        yield return perm;
                    RotateRight(sequence, count);
                }
            }
        }

        private static List<Weave> GetWeaveOrder(ElementalRequirements elementalReqs, List<Weave> praccedWeaves)
        {
            Dictionary<int, List<Weave>> CostPerList = new();

            foreach (var permutation in Permutate(praccedWeaves, praccedWeaves.Count))
            {
                foreach (var weave in permutation)
                {
                    Console.Write(weave.weave + " ");
                }
                int cost = DetermineTotalPracCost(permutation);
                if (!CostPerList.ContainsKey(cost))
                    CostPerList.Add(cost, permutation);
                Console.WriteLine();
            }

            return CostPerList.First().Value;
        }

        private static int DetermineTotalPracCost(List<Weave> weaves)
        {
            int earth = 0;
            int air = 0;
            int fire = 0;
            int water = 0;
            int spirit = 0;

            int pracs = 0;

            foreach (var weave in weaves)
            {
                // Cheating it. This can be fixed by math as well I think.
                for (int i = earth; i < WeaveInfo[weave.weave].Earth; i++) { pracs += (i+1) * 2; earth++; }
                for (int i = air; i < WeaveInfo[weave.weave].Air; i++) { pracs += (i + 1) * 2; air++; }
                for (int i = fire; i < WeaveInfo[weave.weave].Fire; i++) { pracs += (i + 1) * 2; fire++; }
                for (int i = water; i < WeaveInfo[weave.weave].Water; i++) { pracs += (i + 1) * 2; water++; }
                for (int i = spirit; i < WeaveInfo[weave.weave].Spirit; i++) { pracs += (i + 1) * 2; spirit++; }

                while (weave.currentPercentage < weave.percentageToGet)
                {
                    weave.AddOne(earth, air, fire, water, spirit);
                    pracs++;
                }
            }
            
            return pracs;
        }

        private class ReturnObject
        {
            public int intelligence;
            public int rank;
            public List<string> elements = new();
            public int elementPracs;
            public int weavePracs;
            public int totalPracs;
            public int levelRequired;
            public List<string> commands = new();
            public List<Weave> weavesDebug = new();
        }

        private static int GetRate(int intelligence, int currentPercentage, int rank, int bonus, int difficulty)
        {
            int increase = 0;

            if (currentPercentage < 21)
            {
                increase = intelligence - 2;
            }
            else if (currentPercentage < 41)
            {
                switch (intelligence)
                {
                    case 12:
                    case 13:
                        increase = 8;
                        break;
                    case 14:
                        increase = 9;
                        break;
                    case 15:
                        increase = 10;
                        break;
                    case 16:
                        increase = 11;
                        break;
                    case 17:
                    case 18:
                        increase = 12;
                        break;
                    case 19:
                        increase = 13;
                        break;
                }
            }
            else if (currentPercentage < 61)
            {
                switch (intelligence)
                {
                    case 12:
                        increase = 5;
                        break;
                    case 13:
                        increase = 6;
                        break;
                    case 14:
                    case 15:
                        increase = 7;
                        break;
                    case 16:
                    case 17:
                        increase = 8;
                        break;
                    case 18:
                        increase = 9;
                        break;
                    case 19:
                        increase = 10;
                        break;
                }
            }
            else if (currentPercentage < 81)
            {
                switch (intelligence)
                {
                    case 12:
                    case 13:
                    case 14:
                        increase = 4;
                        break;
                    case 15:
                    case 16:
                        increase = 5;
                        break;
                    case 17:
                    case 18:
                    case 19:
                        increase = 6;
                        break;
                }
            }
            else if (currentPercentage < 91)
            {
                switch (intelligence)
                {
                    case 12:
                    case 13:
                    case 14:
                    case 15:
                    case 16:
                        increase = 2;
                        break;
                    case 17:
                    case 18:
                    case 19:
                        increase = 3;
                        break;
                }
            }
            else 
            {    
                increase = 1;
            }

            if (difficulty == 0)
            {
                increase = (int)Math.Floor(0.85 * (double)increase);
            }
            else if (difficulty == 2)
            {
                increase = (int)Math.Floor((double)increase * 1.5);
            }
            if (increase == 0) increase = 1;

            increase = (int)(increase + Math.Floor(0.04 * (double)bonus * increase));
            increase = (int)(increase + Math.Floor(0.04 * (double)rank * increase));

            return increase;
        }

        private class Weave
        {
            public string weave;
            public int percentageToGet;
            public int pracsNeeded;
            public int currentPercentage;
            
            private int intelligence;
            private int rank;

            public Weave(string weave, int intelligence, int rank, int percentageToGet)
            {
                this.weave = weave;
                this.percentageToGet = percentageToGet;
                this.intelligence = intelligence;
                this.rank = rank;
            }

            private int CalculateBonus(int earth, int air, int fire, int water, int spirit)
            {
                int bonus = 0;

                if (WeaveInfo[weave].Earth != 0) bonus += (earth - WeaveInfo[weave].Earth);
                if (WeaveInfo[weave].Air != 0) bonus += (air - WeaveInfo[weave].Air);
                if (WeaveInfo[weave].Fire != 0) bonus += (fire - WeaveInfo[weave].Fire);
                if (WeaveInfo[weave].Water != 0) bonus += (water - WeaveInfo[weave].Water);
                if (WeaveInfo[weave].Spirit != 0) bonus += (spirit - WeaveInfo[weave].Spirit);

                return bonus;
            }

            public void AddOne(int earth, int air, int fire, int water, int spirit)
            {
                int bonus = CalculateBonus(earth, air, fire, water, spirit);
                int increase = GetRate(intelligence, currentPercentage, bonus, this.rank, WeaveInfo[weave].difficulty);

                this.currentPercentage += increase;
            }
        }

        private class WeaveInfoElement
        {
            public int Earth;
            public int Air;
            public int Fire;
            public int Water;
            public int Spirit;
            public int difficulty = 1; // can be 0, 1 or 2
        }

        private class ElementalRequirements
        {
            public int Earth;
            public int Air;
            public int Fire;
            public int Water;
            public int Spirit;

            public IEnumerable<string> GetElementalStringList()
            {
                return new List<string>()
                {
                    $"earth:{Earth}",
                    $"air:{Air}",
                    $"fire:{Fire}",
                    $"water:{Water}",
                    $"spirit:{Spirit}"
                };
            }

            public void UpdateReqs (WeaveInfoElement weaveInfo)
            {
                Earth = Math.Max(Earth, weaveInfo.Earth);
                Air = Math.Max(Air, weaveInfo.Air);
                Fire = Math.Max(Fire, weaveInfo.Fire);
                Water = Math.Max(Water, weaveInfo.Water);
                Spirit = Math.Max(Spirit, weaveInfo.Spirit);
            }

            public int GetElementalPracs()
            {
                int pracs = 0;

                // Cheating it. This can be fixed by math as well I think.
                for (int i = 0; i <= Earth; i++) pracs += i * 2;
                for (int i = 0; i <= Air; i++) pracs += i * 2;
                for (int i = 0; i <= Fire; i++) pracs += i * 2;
                for (int i = 0; i <= Water; i++) pracs += i * 2;
                for (int i = 0; i <= Spirit; i++) pracs += i * 2;

                return pracs;
            }

            public IEnumerable<string> GetElementalCommands()
            {
                List<string> elementalCommands = new();

                for (int i = 0; i < Earth; i++) elementalCommands.Add("practice earth");
                for (int i = 0; i < Air; i++) elementalCommands.Add("practice air");
                for (int i = 0; i < Fire; i++) elementalCommands.Add("practice fire");
                for (int i = 0; i < Water; i++) elementalCommands.Add("practice water");
                for (int i = 0; i < Spirit; i++) elementalCommands.Add("practice spirit");

                return elementalCommands;
            }
        }

        private static readonly Dictionary<string, WeaveInfoElement> WeaveInfo = new()
        {
                {"armor", new WeaveInfoElement() {Spirit = 2} },
                {"blind", new WeaveInfoElement() {Earth = 1, Fire = 1, Spirit = 1} },
                {"call lightning", new WeaveInfoElement() {Air = 3, Fire = 1, Water = 2} },
                {"change weather", new WeaveInfoElement() {Air = 2, Water = 3} },
                {"chill", new WeaveInfoElement() {Water = 1} },
                {"contagion", new WeaveInfoElement() {Earth = 4, Spirit = 3} },
                {"create fog", new WeaveInfoElement() {Air = 2, Water = 3, difficulty = 2} },
                {"create food", new WeaveInfoElement() {Earth = 1} },
                {"create phantom object", new WeaveInfoElement() {Earth = 3} },
                {"create water", new WeaveInfoElement() {Water = 1} },
                {"cure blindness", new WeaveInfoElement() {Fire = 1, Spirit = 3, difficulty = 2} },
                {"cure critical wounds", new WeaveInfoElement() {Earth = 1, Water = 5, Spirit = 2} },
                {"cure fear", new WeaveInfoElement() {Spirit = 4} },
                {"cure light wounds", new WeaveInfoElement() {Earth = 1, Water = 1, Spirit = 2, difficulty = 2} },
                {"cure poison", new WeaveInfoElement() {Earth = 4, Water = 3, difficulty = 2} },
                {"cure serious wounds", new WeaveInfoElement() {Earth = 1, Water = 4, Spirit = 2} },
                {"deafen", new WeaveInfoElement() {Earth = 2, Spirit = 1, difficulty = 2} },
                {"earthquake", new WeaveInfoElement() {Earth = 4} },
                {"elemental staff", new WeaveInfoElement() {Air = 4, Fire = 3, Water = 5} },
                {"fear", new WeaveInfoElement() {Spirit = 2} },
                {"fireball", new WeaveInfoElement() {Fire = 7} },
                {"flame strike", new WeaveInfoElement() {Fire = 4} },
                {"freeze", new WeaveInfoElement() {Air = 3, Spirit = 5} },
                {"gate", new WeaveInfoElement() {Earth = 7, Spirit = 4, difficulty = 0 } },
                {"hailstorm", new WeaveInfoElement() {Air = 1, Water = 4} },
                {"hammer of air", new WeaveInfoElement() {Air = 4} },
                {"heal", new WeaveInfoElement() {Earth = 1, Water = 6, Spirit = 2} },
                {"hurricane", new WeaveInfoElement() {Air = 6} },
                {"ice spikes", new WeaveInfoElement() {Air = 3, Water = 3} },
                {"incinerate", new WeaveInfoElement() {Earth = 7, Fire = 7, Spirit = 7} },
                {"light ball", new WeaveInfoElement() {Air = 1, Fire = 1} },
                {"locate life", new WeaveInfoElement() {Earth = 2, Air = 2, Water = 4, Spirit = 2} },
                {"locate object", new WeaveInfoElement() {Earth = 4, Air = 2, Spirit = 2} },
                {"poison", new WeaveInfoElement() {Earth = 4, Water = 3, difficulty = 2} },
                {"refresh", new WeaveInfoElement() {Earth = 2, Water = 2, Spirit = 3} },
                {"remove contagion", new WeaveInfoElement() {Earth = 2, Spirit = 2, difficulty = 2} },
                {"remove warding", new WeaveInfoElement() {Earth = 1, Air = 1, Spirit = 3} },
                {"sense warding", new WeaveInfoElement() {Spirit = 2, difficulty = 2} },
                {"shield", new WeaveInfoElement() {Spirit = 5} },
                {"silence", new WeaveInfoElement() {Earth = 2, Spirit = 1, difficulty = 2} },
                {"sleep", new WeaveInfoElement() {Air = 3, Spirit = 5} },
                {"slice weaves", new WeaveInfoElement() {Earth = 1, Air = 1, Fire = 4, Spirit = 2} },
                {"slow", new WeaveInfoElement() {Air = 3, Spirit = 5} },
                {"sonic boom", new WeaveInfoElement() {Air = 3, Fire = 1, Water = 2} },
                {"strength", new WeaveInfoElement() {Earth = 3, Water = 3, Spirit = 2} },
                {"sword of flame", new WeaveInfoElement() {Earth = 3, Air = 4, Fire = 5} },
                {"travel", new WeaveInfoElement() {Earth = 7, Spirit = 2, difficulty = 0 } },
                {"ward object", new WeaveInfoElement() {Spirit = 3, difficulty = 2 } },
                {"warding vs damage", new WeaveInfoElement() {Earth = 4, Air = 4, Water = 4} },
                {"warding vs evil", new WeaveInfoElement() {Spirit = 1} },
                {"whirlpool", new WeaveInfoElement() {Water = 5} }
            };

        private static int GetLevelRequired(int pracsNeeded)
        {
            int Start_Prac = 3;
            int Start_Level = 1;
            while (Start_Prac < pracsNeeded)
            {
                Start_Level++;
                if (Start_Level < 11)
                {
                    Start_Prac += 3;
                }
                else if (Start_Level < 16)
                {
                    Start_Prac += 5;
                }
                else if (Start_Level < 26)
                {
                    Start_Prac += 7;
                }
                else if (Start_Level < 41)
                {
                    Start_Prac += 8;
                }
                else
                {
                    Start_Prac += 2;
                }
            }

            return Start_Level;
        }
    }
}

