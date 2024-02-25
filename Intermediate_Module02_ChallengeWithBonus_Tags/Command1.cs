#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

#endregion

namespace Intermediate_Module02_ChallengeWithBonus_Tags
{
    [Transaction(TransactionMode.Manual)]
    public class Command1 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // this is a variable for the Revit application
            UIApplication uiapp = commandData.Application;

            // this is a variable for the current Revit model
            Document doc = uiapp.ActiveUIDocument.Document;

            //0. Get all the views
            FilteredElementCollector viewCollector = new FilteredElementCollector(doc);
            viewCollector.OfCategory(BuiltInCategory.OST_Views);
            viewCollector.WhereElementIsNotElementType();

            List<View> viewToTag = new List<View>();
            foreach (View view in viewCollector)
            {
                if (!view.IsTemplate)
                {
                    if (view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.CeilingPlan || view.ViewType == ViewType.Section || view.ViewType == ViewType.AreaPlan)
                    {
                        viewToTag.Add(view);
                    }
                }
            }

            int counter = 0;

            foreach (View curView in viewToTag)
            {

                // 1. get the current view type
                ViewType curViewType = curView.ViewType;


                // 2. get the categories for the view type
                List<BuiltInCategory> catList = new List<BuiltInCategory>();
                Dictionary<ViewType, List<BuiltInCategory>> viewTypeCatDictionary = GetViewTypeCatDictionary();

                //if (!viewTypeCatDictionary.TryGetValue(curViewType, out catList)) //- these two would  be the same thing
                if (viewTypeCatDictionary.TryGetValue(curViewType, out catList) == false)
                {
                    continue;
                }

                // 3. get elements to tag for the view type
                ElementMulticategoryFilter catFilter = new ElementMulticategoryFilter(catList);
                FilteredElementCollector elemCollector = new FilteredElementCollector(doc, curView.Id)
                    .WherePasses(catFilter)
                    .WhereElementIsNotElementType();


                //TaskDialog.Show("test", $"Found {elemCollector.GetElementCount()} element");

                // 6. get dictionary of tag family symbols
                Dictionary<string, FamilySymbol> tagDictionary = GetTagDictionary(doc);

                // 4. loop through elements and tag


                using (Transaction t = new Transaction(doc))
                {
                    t.Start("Tag elements");
                    foreach (Element curElem in elemCollector)
                    {
                        bool addLeader = false;

                        if (curElem.Location == null)
                            continue;

                        // 5. get insertion point based on element type
                        XYZ point = GetInsertPoint(curElem.Location);

                        if (point == null)
                            continue;

                        // 7. get element data
                        string catName = curElem.Category.Name;

                        // 10. check cat name for walls
                        if (catName == "Walls")
                        {
                            addLeader = true;

                            if (IsCurtainWall(curElem))
                                catName = "Curtain Walls";
                        }

                        // 8. get tag based on element type
                        if (!tagDictionary.TryGetValue(catName, out FamilySymbol elemTag))
                            continue;

                        //FamilySymbol elemTag = tagDictionary[catName];


                        // 9. tag element
                        if (catName == "Areas")
                        {
                            ViewPlan curAreaPlan = curView as ViewPlan;
                            Area curArea = curElem as Area;

                            AreaTag newTag = doc.Create.NewAreaTag(curAreaPlan, curArea, new UV(point.X, point.Y));
                            newTag.TagHeadPosition = new XYZ(point.X, point.Y, 0);
                        }
                        else
                        {
                            IndependentTag newTag = IndependentTag.Create(doc, elemTag.Id, curView.Id,
                                new Reference(curElem), addLeader, TagOrientation.Horizontal, point);

                            // 9a. offset tags as needed
                            if (catName == "Windows")
                                newTag.TagHeadPosition = point.Add(new XYZ(0, 3, 0));

                            if (curView.ViewType == ViewType.Section)
                                newTag.TagHeadPosition = point.Add(new XYZ(0, 0, 3));
                        }

                        counter++;
                    }
                    t.Commit();
                }

                TaskDialog.Show("Complete", $"Added {counter} tags to the view");

            }

            return Result.Succeeded;
        }

        private bool IsCurtainWall(Element curElem)
        {
            Wall curWall = curElem as Wall;

            if (curWall.WallType.Kind == WallKind.Curtain)
                return true;

            return false;
        }

        private Dictionary<string, FamilySymbol> GetTagDictionary(Document doc)
        {
            return new Dictionary<string, FamilySymbol>
            {
                { "Rooms", GetTagByName(doc, "M_Room Tag") },
                { "Doors", GetTagByName(doc, "M_Door Tag") },
                { "Windows", GetTagByName(doc, "M_Window Tag") },
                { "Furniture", GetTagByName(doc, "M_Furniture Tag") },
                { "Lighting Fixtures", GetTagByName(doc, "M_Lighting Fixture Tag") },
                { "Walls", GetTagByName(doc, "M_Wall Tag") },
                { "Curtain Walls", GetTagByName(doc, "M_Curtain Wall Tag") },
                { "Areas", GetTagByName(doc, "M_Area Tag") }
            };
        }

        private FamilySymbol GetTagByName(Document doc, string tagName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.FamilyName.Equals(tagName))
                .First();
        }

        private XYZ GetInsertPoint(Location loc)
        {
            LocationPoint locPoint = loc as LocationPoint;
            XYZ point;

            if (locPoint != null)
            {
                point = locPoint.Point;
            }
            else
            {
                LocationCurve locCurve = loc as LocationCurve;
                point = MidpointBetweenTwoPoints(locCurve.Curve.GetEndPoint(0), locCurve.Curve.GetEndPoint(1));

            }

            return point;
        }

        private XYZ MidpointBetweenTwoPoints(XYZ point1, XYZ point2)
        {
            XYZ midPoint = new XYZ((point1.X + point2.X) / 2, (point1.Y + point2.Y) / 2, (point1.Z + point2.Z) / 2);
            return midPoint;
        }

        private Dictionary<ViewType, List<BuiltInCategory>> GetViewTypeCatDictionary()
        {
            Dictionary<ViewType, List<BuiltInCategory>> dictionary = new Dictionary<ViewType, List<BuiltInCategory>>();

            dictionary.Add(ViewType.FloorPlan, new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Rooms,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_Walls
            });

            dictionary.Add(ViewType.AreaPlan, new List<BuiltInCategory> { BuiltInCategory.OST_Areas });

            dictionary.Add(ViewType.CeilingPlan, new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Rooms,
                BuiltInCategory.OST_LightingFixtures
            });

            dictionary.Add(ViewType.Section, new List<BuiltInCategory> { BuiltInCategory.OST_Rooms });

            return dictionary;
        }
        internal static PushButtonData GetButtonData()
        {
            // use this method to define the properties for this command in the Revit ribbon
            string buttonInternalName = "btnCommand1";
            string buttonTitle = "Button 1";

            ButtonDataClass myButtonData1 = new ButtonDataClass(
                buttonInternalName,
                buttonTitle,
                MethodBase.GetCurrentMethod().DeclaringType?.FullName,
                Properties.Resources.Blue_32,
                Properties.Resources.Blue_16,
                "This is a tooltip for Button 1");

            return myButtonData1.Data;
        }
    }
}
