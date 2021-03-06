﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections;
using System.Xml;
using System.IO;
using System.Xml.Linq;
using CommandLine;
using CommandLine.Text;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace GFilterImporter
{
    class Program
    {
        // Command Line Options
        class Options
        {
            [Option('f', "file", Required = true, HelpText = "Input file to read.")]
            public string InputFile { get; set; }

            [Option('u', "user", Required = true, HelpText = "Specific User.")]
            public string UserName { get; set; }

            [Option('v', null, HelpText = "Print details during execution.")]
            public bool Verbose { get; set; }

            [HelpOption]
            public string GetUsage()
            {
                // this without using CommandLine.Text
                //  or using HelpText.AutoBuild
                var usage = new StringBuilder();
                usage.AppendLine(
                    String.Format("{0} 1.0", System.Reflection.Assembly.GetExecutingAssembly().GetName().Name));
                usage.AppendLine("-f for file");
                usage.AppendLine("-u for file");
                return usage.ToString();
            }
        }

        // MAIN
        static void Main(string[] args)
        {

            var options = new Options();
            if (CommandLine.Parser.Default.ParseArguments(args, options))
            {

                // Here we verify the Exchange account exists
                Outlook.Application outlook = new Outlook.Application();
                Outlook.AddressEntry currentUser;
                currentUser = null;

                try
                {
                    currentUser = outlook.Session.CurrentUser.AddressEntry;
                }
                catch
                {
                    OutputColor(ConsoleColor.Red, "Outlook is required to run this application!");
                }

                if (currentUser.Type != "EX")
                    OutputColor(ConsoleColor.Red, "Current user is not an exchange user.\n");

                var excUser = currentUser.GetExchangeUser();
                if (excUser == null)
                    OutputColor(ConsoleColor.Red, "No current exchange user...\n");

                Console.Write("Current user: ");
                OutputColor(ConsoleColor.Red, excUser.Name + "\n"); 

                Console.Write("Creating exchange rules on account: ");
                OutputColor(ConsoleColor.Magenta, options.UserName + "\n");

                // Get our account id (specified by email)
                Outlook.Stores stores = outlook.Application.Session.Stores;
                string storeId = null;
                foreach (Outlook.Store store in stores)
                {
                    if (options.UserName.Trim() == store.DisplayName.Trim())
                    {
                        storeId = store.StoreID;
                    }
                }

                ParseFilters(options.Verbose, options.InputFile, options.UserName, outlook, storeId);
            }

        } // end main

        ///
        public static String ParseFilters(bool verbose, string mailFilters, string user,
            Outlook.Application outlook, string storeId)
        {

            string mailFilter = mailFilters;
            // Test file type
            if (Path.GetExtension(mailFilter) != ".xml")
            {
                OutputColor(ConsoleColor.Red, "Not an XML file.\n");
                System.Environment.Exit(1);
            }

            if (File.Exists(@mailFilter))
            {
                // XML Document Loader
                XDocument doc = XDocument.Load(@mailFilter);
                XNamespace apps = "http://schemas.google.com/apps/2006";
                XNamespace ns = "http://www.w3.org/2005/Atom";

                if (verbose)
                {
                    Console.Write("Parsing file ");
                    OutputColor(ConsoleColor.Magenta, mailFilter + "\n");
                }

                // create our new list
                foreach (XElement entry in doc.Descendants(ns + "entry"))
                {

                    Entry filterValue = new Entry();

                    foreach (XElement rule in entry.Descendants(apps + "property"))
                    {

                        var tag = rule.Attribute("name").Value;
                        var val = rule.Attribute("value").Value;

                        if (tag == "from" && !String.IsNullOrEmpty(val))
                        {
                            filterValue.From = val;
                        }
                        else if (tag == "label" && !String.IsNullOrEmpty(val))
                        {
                            filterValue.Folder = val;
                        }
                        else
                        {
                            continue;
                        }

                        if (filterValue.From != null && filterValue.Folder != null)
                        {
                            // Create the exchange rule!
                            CreateExchangeRule(filterValue.Folder, filterValue.From, user, verbose, outlook, storeId);
                        }
                    }
                }
            }
            else
            {
                if (verbose)
                {
                    Console.Write("The File {0} does not exist.", mailFilter);
                    OutputColor(ConsoleColor.Red, mailFilter.ToString() + "\n");
                }
            }

            return null;
        }

        // Create Rule
        public static String CreateExchangeRule(string folder, string email, string exUser, 
            bool verbose, Outlook.Application outlook, string storeId)
        {
            // If filter is a sub-folder it creates filters as Name/Subfolder
            char[] split = {'/'};
            string[] folderPath = folder.Split(split);


            // Retrieve current rules
            Outlook.Rules rules;
            Outlook.Rule folderRule;

            try
            {
                folderRule = outlook.Application.Session.GetStoreFromID(storeId).GetRules()[folder] as Outlook.Rule;
            }
            catch (Exception e)
            {
                if (e.HResult == -2147221233)
                {
                    folderRule = null;
                } else {
                    Console.WriteLine("Rule already exists: \n{0}", e.Message);
                    return null;
                }
            }

            // here
            try
            {
                rules = outlook.Application.Session.GetStoreFromID(storeId).GetRules();
            }
            catch 
            {
                OutputColor(ConsoleColor.Red, "Could not obtain rules collection.\n");
                return null;
            }

            if (folderRule == null)
            {
                // folder rule doesn't exist, let the user know
                OutputColor(ConsoleColor.Magenta, "Rule doesn't exist, creating new rule.\n");

                // Default Folder
                Outlook.Folder newFolder;
                newFolder = null;

                for (int i = 0; i < folderPath.Length; i++)
                {
                    try
                    {
                        try
                        {
                            if (i == 0)
                            {
                                newFolder = outlook.Session.GetStoreFromID(storeId).GetRootFolder().Folders[folderPath[i]] as Outlook.Folder;
                            }
                            else
                            {
                                newFolder = newFolder.Folders[folderPath[i]] as Outlook.Folder;
                            }
                        }
                        catch
                        {
                            newFolder = newFolder.Folders[folderPath[i]] as Outlook.Folder;
                        }
                    }
                    catch
                    {
                        try
                        {
                            newFolder = newFolder.Folders.Add(folderPath[i], Type.Missing) as Outlook.Folder;
                        }
                        catch
                        {
                            newFolder = outlook.Session.GetStoreFromID(storeId).GetRootFolder().Folders.Add(folderPath[i], Type.Missing) as Outlook.Folder;
                        }
                    }
                }

                // Take the folder and the email name and parse it into a usable rule.
                if (verbose)
                {
                    Console.Write("We have Folder: ");
                    OutputColor(ConsoleColor.Magenta, newFolder.Name + "\n");
                    Console.Write("We have From: ");
                    OutputColor(ConsoleColor.Magenta, email + "\n");
                }

                // Now lets create our rule
                Outlook.Rule rule = rules.Create(folder,
                    Outlook.OlRuleType.olRuleReceive);

                // From from the google label
                Object[] addressCondition = { email };

                rule.Conditions.SenderAddress.Address = addressCondition;
                rule.Conditions.SenderAddress.Enabled = true;

                // from is basic
                //rule.Conditions.From.Recipients.Add(email);
                //rule.Conditions.From.Enabled = true;

                // What folder we want to move the email to.
                rule.Actions.MoveToFolder.Folder = newFolder;
                rule.Actions.MoveToFolder.Enabled = true;
                // Stop after processing this.
                rule.Actions.Stop.Enabled = true;

                try
                {
                    rules.Save(true);
                }
                catch (Exception ex)
                {
                    OutputColor(ConsoleColor.Red, ex.Message + "\n");
                }
            }
            else
            {
                OutputColor(ConsoleColor.Red, "Rule already exists. \n");
            }

            return null;
        }

        //Colorssss
        public static void OutputColor(ConsoleColor color, string text)
        {
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            // Specify background color?
            //Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(text);
            Console.ForegroundColor = originalColor;
        }

        //Entry class
        class Entry
        {
            public string From;
            public string Folder;
        }

    }
}