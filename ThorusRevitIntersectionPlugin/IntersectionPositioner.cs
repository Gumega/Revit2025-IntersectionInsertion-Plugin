using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

namespace ThorusRevitIntersectionPlugin
{
	[Transaction(TransactionMode.Manual)]
	public class IntersectionPositioner : IExternalCommand
	{
		public string slabHoleFamilyName = "FURO-QUADRADO-LAJE";
		private List<Element>? tubeElementArray;
		private Element? slabElement;

		public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
		{
			UIApplication uiapp = commandData.Application;
			Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;

			List<Document> openProjectList = GetOpenProjectDocuments(app);
			if (openProjectList.Count == 0)
			{
				Autodesk.Revit.UI.TaskDialog.Show("Warning", "There are no open files.");
				return Result.Cancelled;
			}

			if (!GetHoleDocumentAndFamily(openProjectList, out Document? holeDocument, out Family? holeFamily))
			{
				return Result.Failed;
			}

			using (Win wpfForm = new(openProjectList))
			{
				wpfForm.ShowDialog();

				if (wpfForm.tubesDocument == null || wpfForm.structureDocument == null)
				{
					return Result.Cancelled;
				}

				slabElement = new FilteredElementCollector(wpfForm.structureDocument)
					.OfClass(typeof(Floor))
					.WhereElementIsNotElementType()
					.FirstOrDefault();

				if (slabElement == null)
				{
					Autodesk.Revit.UI.TaskDialog.Show("Warning", "Could not find a floor/slab within the structural file.");
					return Result.Failed;
				}

				Solid? slabSolid = GetSolidListFromElement(slabElement).FirstOrDefault(s => s.Volume > 0);
				if (slabSolid == null)
				{
					Autodesk.Revit.UI.TaskDialog.Show("Warning", "Slab has no solid or volume.");
					return Result.Failed;
				}

				tubeElementArray = GetMepElements(wpfForm.tubesDocument);

				double clearanceOffsetFeet = UnitUtils.Convert(0.05, UnitTypeId.Meters, UnitTypeId.Feet);
				double slabThicknessFeet = GetSlabThickness(wpfForm.structureDocument, slabElement);
				double totalHoleLengthFeet = slabThicknessFeet + (clearanceOffsetFeet * 2.0);

				ProcessIntersections(wpfForm.structureDocument, holeDocument!, slabSolid, clearanceOffsetFeet, totalHoleLengthFeet, slabThicknessFeet);
			}

			return Result.Succeeded;
		}

		#region Document & Element Collectors

		private List<Document> GetOpenProjectDocuments(Autodesk.Revit.ApplicationServices.Application app)
		{
			return app.Documents
				.Cast<Document>()
				.Where(doc => !doc.IsFamilyDocument)
				.ToList();
		}

		private bool GetHoleDocumentAndFamily(List<Document> openProjects, out Document? holeDocument, out Family? holeFamily)
		{
			holeDocument = openProjects.FirstOrDefault(p => p.Title.ToLower().Contains("furação"));
			holeFamily = null;

			if (holeDocument == null)
			{
				Autodesk.Revit.UI.TaskDialog.Show("Warning", "No opened file with name 'Furação'.");
				return false;
			}

			holeFamily = new FilteredElementCollector(holeDocument)
				.OfClass(typeof(Family))
				.Cast<Family>()
				.FirstOrDefault(f => f.Name == slabHoleFamilyName);

			if (holeFamily == null)
			{
				Autodesk.Revit.UI.TaskDialog.Show("Warning", $"Unable to find '{slabHoleFamilyName}' family in the Furação document.");
				return false;
			}

			return true;
		}

		private List<Element> GetMepElements(Document mepDocument)
		{
			List<Element> mepElements = [.. new FilteredElementCollector(mepDocument)
				.OfClass(typeof(Pipe))
				.WhereElementIsNotElementType()];

			mepElements.AddRange(
				new FilteredElementCollector(mepDocument)
					.OfClass(typeof(Conduit))
					.WhereElementIsNotElementType()
			);

			return mepElements;
		}

		private double GetSlabThickness(Document structureDocument, Element slab)
		{
			if (slab is Floor floor)
			{
				FloorType? slabType = structureDocument.GetElement(floor.FloorType.Id) as FloorType;
				if (slabType != null)
				{
					Parameter thicknessParameter = slabType.get_Parameter(BuiltInParameter.STRUCTURAL_FLOOR_CORE_THICKNESS)
												?? slabType.get_Parameter(BuiltInParameter.FLOOR_ATTR_DEFAULT_THICKNESS_PARAM);

					if (thicknessParameter != null)
					{
						return thicknessParameter.AsDouble();
					}
				}
			}
			return 0;
		}
		#endregion

		#region Geometry Operations
		private List<Solid> GetSolidListFromElement(Element element)
		{
			List<Solid> validSolids = new();
			Options geometryOptions = new() { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
			GeometryElement geometryElement = element.get_Geometry(geometryOptions);

			if (geometryElement == null) return validSolids;
			GetSolidListFromGeometry(geometryElement, ref validSolids);

			return validSolids;
		}

		private void GetSolidListFromGeometry(GeometryElement geometryElement, ref List<Solid> validSolidList)
		{
			foreach (GeometryObject item in geometryElement)
			{
				if (item is Solid solid && solid.Volume > 0)
				{
					validSolidList.Add(solid);
				}
				else if (item is GeometryInstance geometryInstance)
				{
					foreach (GeometryObject innerItem in geometryInstance.GetInstanceGeometry())
					{
						if (innerItem is Solid innerSolid && innerSolid.Volume > 0)
						{
							validSolidList.Add(innerSolid);
						}
					}
				}
			}
		}

		private void ProcessIntersections(Document targetDocument, Document holeDocument, Solid slabSolid, double clearanceOffset, double totalHoleLength, double slabThickness)
		{
			if (tubeElementArray == null) return;

			foreach (Element tubeElement in tubeElementArray)
			{
				List<Solid> tubeSolids = GetSolidListFromElement(tubeElement);
				foreach (Solid solid in tubeSolids)
				{
					try
					{
						Solid intersectionVolume = BooleanOperationsUtils.ExecuteBooleanOperation(slabSolid, solid, BooleanOperationsType.Intersect);
						if (intersectionVolume == null || intersectionVolume.Volume <= 0.00001)
							continue;

						XYZ intersectionPoint = intersectionVolume.GetBoundingBox().Transform.Origin;
						InsertModel(targetDocument, holeDocument, intersectionPoint, clearanceOffset, totalHoleLength, slabThickness);
					}
					catch
					{
						continue;
					}
				}
			}
		}
		#endregion

		#region Family Placement
		private bool InsertModel(Document targetDocument, Document holeDocument, XYZ location, double clearanceOffset, double totalHoleLength, double slabThickness)
		{
			using (Transaction transaction = new(targetDocument, "Insert tube intersection"))
			{
				transaction.Start();

				FamilySymbol? holeSymbol = GetOrCopyHoleSymbol(targetDocument, holeDocument);
				if (holeSymbol == null)
				{
					transaction.RollBack();
					return false;
				}

				if (!holeSymbol.IsActive)
				{
					holeSymbol.Activate();
					targetDocument.Regenerate();
				}

				double zBaseHole = location.Z + (slabThickness / 2.0) + clearanceOffset;
				XYZ adjustedInsertionPoint = new(location.X, location.Y, zBaseHole);

				Level? slabLevel = slabElement != null ? targetDocument.GetElement(slabElement.LevelId) as Level : null;

				FamilyInstance newHoleInstance = targetDocument.Create.NewFamilyInstance(
					adjustedInsertionPoint,
					holeSymbol,
					slabLevel,
					Autodesk.Revit.DB.Structure.StructuralType.NonStructural
				);
				Parameter lengthParameter = newHoleInstance.LookupParameter("FUR.esp-laje")
										 ?? holeSymbol.LookupParameter("FUR.esp-laje");
				if (lengthParameter != null && !lengthParameter.IsReadOnly)
				{
					lengthParameter.Set(totalHoleLength);
				}
				targetDocument.Regenerate();

				AdjustHoleInstancePosition(newHoleInstance, location.Z);

				targetDocument.Regenerate();
				transaction.Commit();
			}
			return true;
		}

		private FamilySymbol? GetOrCopyHoleSymbol(Document targetDocument, Document sourceDocument)
		{
			FamilySymbol? holeSymbol = new FilteredElementCollector(targetDocument)
				.OfClass(typeof(FamilySymbol))
				.Cast<FamilySymbol>()
				.FirstOrDefault(s => s.FamilyName == slabHoleFamilyName);

			if (holeSymbol == null)
			{
				FamilySymbol? sourceHoleSymbol = new FilteredElementCollector(sourceDocument)
					.OfClass(typeof(FamilySymbol))
					.Cast<FamilySymbol>()
					.FirstOrDefault(s => s.FamilyName == slabHoleFamilyName);

				if (sourceHoleSymbol == null)
				{
					Autodesk.Revit.UI.TaskDialog.Show("Error", $"No Symbol for family '{slabHoleFamilyName}' was found in the source document.");
					return null;
				}

				ICollection<ElementId> copiedElementIds = ElementTransformUtils.CopyElements(
					sourceDocument,
					new List<ElementId> { sourceHoleSymbol.Id },
					targetDocument,
					Transform.Identity,
					new CopyPasteOptions()
				);

				if (copiedElementIds != null && copiedElementIds.Count > 0)
				{
					holeSymbol = targetDocument.GetElement(copiedElementIds.First()) as FamilySymbol;
				}

				if (holeSymbol == null)
				{
					holeSymbol = new FilteredElementCollector(targetDocument)
						.OfClass(typeof(FamilySymbol))
						.Cast<FamilySymbol>()
						.FirstOrDefault(s => s.FamilyName == slabHoleFamilyName);
				}
			}

			if (holeSymbol == null)
			{
				Autodesk.Revit.UI.TaskDialog.Show("Error", "Failed to load the hole FamilySymbol into the current document.");
			}

			return holeSymbol;
		}

		private void AdjustHoleInstancePosition(FamilyInstance holeInstance, double targetCenterZ)
		{
			BoundingBoxXYZ bbox = holeInstance.get_BoundingBox(null);
			if (bbox == null) return;

			double currentRealTopZ = bbox.Max.Z;
			double currentRealBottomZ = bbox.Min.Z;
			double currentRealCenterZ = (currentRealTopZ + currentRealBottomZ) / 2.0;

			double requiredOffset = targetCenterZ - currentRealCenterZ;

			Parameter offsetParameter = holeInstance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
			if (offsetParameter != null && !offsetParameter.IsReadOnly)
			{
				double currentOffset = offsetParameter.AsDouble();
				offsetParameter.Set(currentOffset + requiredOffset);
			}
		}
		#endregion
	}
}