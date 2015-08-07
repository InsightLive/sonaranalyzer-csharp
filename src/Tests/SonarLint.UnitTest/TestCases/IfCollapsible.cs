﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests.TestCases
{
    class IfCollapsible
    {
        public void Test(bool cond1, bool cond2, bool cond3)
        {
            while (cond1)
            {
                if (cond2 || cond3)
                {
                }
            }

            if (cond1)
            {
                if (cond2 || cond3) // Noncompliant
                {
                }
            }
            if (cond1)
                if (cond2 || cond3) // Noncompliant
                {
                }
            if (cond1)
            {
                if (cond2 || cond3)
                {
                }
                else
                {

                }
            }
            if (cond1)
            {
                var x = 5;
                if (cond2 || cond3)
                {
                }
            }

            if (cond1 && (cond2 || cond3))
            {
            }

            if (cond1)
            {
                if (cond2 || cond3) // Compliant, parent has else
                {
                }
            }
            else
            {
            }
        }
    }
}
