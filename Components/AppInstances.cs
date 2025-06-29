using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarvinsAIRARefactored.Components
{
    public static class AppInstances
    {
        public static string RefactorID;

        private static Mutex _mutex;

        public static byte Check()
        {

            bool isNewInstance;
            _mutex = new Mutex(true, "MAIRA_Refactored", out isNewInstance);

            if (isNewInstance)
            {
                //now check for original MAIRA

                var processes = Process.GetProcesses().FirstOrDefault(x => x.ProcessName.ToLower() == "marvinsaira");

                if (processes == null)
                    return 0;

                return 2;

            }
            return 1;
        }
    }
}
