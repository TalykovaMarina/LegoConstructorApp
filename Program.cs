using System;
using System.Windows.Forms;

namespace LegoConstructorApp;

internal static class Program
{
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}