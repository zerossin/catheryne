using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera
{
	public static class UserInterface
	{
		internal static int HeadlessErrors;
 private static int headlessWeapons,headlessArtifacts,headlessCharacters;
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		// Artifacts and Weapons
		private static PictureBox gear_PictureBox;

		private static TextBox gear_TextBox;

		// Character
		private static PictureBox cName_PictureBox;

		private static PictureBox cLevel_PictureBox;
		private static PictureBox[] cTalent_PictureBoxes = new PictureBox[3];
		private static TextBox character_TextBox;

		// Counters
		private static Label weaponCount_Label;

		private static Label weaponMax_Label;

		private static Label artifactCount_Label;
		private static Label artifactMax_Label;

		private static Label characterCount_Label;

		// Status
		private static Label programStatus_Label;

		// Error box
		private static TextBox error_TextBox;

		// Current Images
		private static PictureBox navigation_PictureBox;

		public static void Init(PictureBox _gear_PictureBox, TextBox _a_textbox, PictureBox _c_name, PictureBox _c_level, PictureBox[] _c_talent, TextBox _c_textbox, Label _weaponCount, Label _weaponMax, Label _artifactCount, Label _artifactMax, Label _characterCount, Label _programStatus, TextBox _error_textBox, PictureBox _navigation_Image)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			// Artifacts and Weapons
			gear_PictureBox = _gear_PictureBox;
			gear_TextBox = _a_textbox;

			// Characters
			cName_PictureBox = _c_name;
			cLevel_PictureBox = _c_level;
			cTalent_PictureBoxes = _c_talent;
			character_TextBox = _c_textbox;

			// Counters
			weaponCount_Label = _weaponCount;
			weaponMax_Label = _weaponMax;
			artifactCount_Label = _artifactCount;
			artifactMax_Label = _artifactMax;
			characterCount_Label = _characterCount;

			// Status
			programStatus_Label = _programStatus;

			// Error
			error_TextBox = _error_textBox;

			// Navigation Image
			navigation_PictureBox = _navigation_Image;
		}

		private static void UpdateElements(Bitmap bm, string text, PictureBox pictureBox, TextBox textBox)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdatePictureBox(bm, pictureBox);
			UpdateTextBox(text, textBox);
		}

		private static void UpdatePictureBox(Bitmap bm, PictureBox pictureBox)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			try
			{
				Bitmap clone = new Bitmap(bm.Width, bm.Height);
				using (var copy = Graphics.FromImage(clone))
				{
					copy.DrawImage(bm, 0, 0);
				}
				MethodInvoker pictureBoxAction = delegate
			{
				pictureBox.Image = clone;
				pictureBox.Refresh();
			};
				pictureBox.Invoke(pictureBoxAction);
			}
			catch (Exception e)
			{
				Logger.Debug($"Problem updating picturebox {0}\n{1}", pictureBox.Name, e);
			}
		}

		private static void UpdateTextBox(string text, TextBox textBox)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			try
			{
				MethodInvoker textBoxAction = delegate
				{
					textBox.AppendText(text.Replace("\n", Environment.NewLine));
					textBox.AppendText(Environment.NewLine);
					textBox.Refresh();
				};
				textBox.Invoke(textBoxAction);
			}
			catch (Exception e)
			{
				Logger.Debug($"Problem updating picturebox {0}\n{1}", textBox.Name, e);
			}
		}

		private static void UpdateLabel(string text, Label label)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			try
			{
				MethodInvoker labelAction =  delegate
			{
				label.Text = text;
				label.Refresh();
			};
				label.Invoke(labelAction);
			}
			catch (Exception e)
			{
				Logger.Debug($"Problem updating picturebox {0}\n{1}", label.Name, e);

			}
		}

		public static void SetGear(Bitmap bm, Weapon weapon)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			ResetGearDisplay();
			SetGearPictureBox(bm);
			SetGearTextBox(weapon.ToString());
		}

		public static void SetGear(Bitmap bm, Artifact artifact)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			ResetGearDisplay();
			SetGearPictureBox(bm);
			SetGearTextBox(artifact.ToString());
		}

		public static void SetGearPictureBox(Bitmap bm)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdatePictureBox(bm, gear_PictureBox);
		}

		public static void SetGearTextBox(string text)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateTextBox(text, gear_TextBox);
		}

		internal static void SetMainCharacterName(string text)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateTextBox($"Traveler name: {text}", character_TextBox);
		}

		public static void SetCharacter_NameAndElement(Bitmap bm, string name, string element)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateElements(bm, $"Name: {name}\nElement: {element}", cName_PictureBox, character_TextBox);
		}

		public static void SetCharacter_Level(Bitmap bm, int level, int maxLevel)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateElements(bm, $"Level: {level} / {maxLevel}", cLevel_PictureBox, character_TextBox);
		}

		public static void SetCharacter_Constellation(int level)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateTextBox($"Constellation: {level}", character_TextBox);
		}

		internal static void SetMaterial(Bitmap nameplate, Bitmap quantity, string name, int count)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateElements(nameplate, $"Name: {name}", cName_PictureBox, character_TextBox);
			UpdateElements(quantity, $"Count: {count}", cLevel_PictureBox, character_TextBox);
		}

		public static void SetMora(Bitmap mora, int count)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateElements(mora, $"Mora: {count}", navigation_PictureBox, character_TextBox);
		}


		public static void SetCharacter_Talent(Bitmap bm, string text, int i)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			if (i > -1 && i < 3)
			{
				UpdatePictureBox(bm, cTalent_PictureBoxes[i]);
				UpdateTextBox($"Talent {i + 1}: {text}", character_TextBox);
			}
		}

		public static void SetWeapon_Max(int value)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateLabel(value.ToString(), weaponMax_Label);
			Logger.Info("Parsed {value} weapons to scan", value);
		}

		public static void SetArtifact_Max(int value)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdateLabel(value.ToString(), artifactMax_Label);
			Logger.Info("Parsed {value} artifacts to scan", value);
		}

		public static void IncrementArtifactCount()
		{
 if(CatheryneScanning.ScanBridge.Active){CatheryneScanning.ScanBridge.Write("running","성유물",System.Threading.Interlocked.Increment(ref headlessArtifacts));return;}
			lock (artifactCount_Label)
			{
				UpdateLabel($"{Int32.Parse(artifactCount_Label.Text) + 1}", artifactCount_Label);
			}
		}

		public static void IncrementWeaponCount()
		{
 if(CatheryneScanning.ScanBridge.Active){CatheryneScanning.ScanBridge.Write("running","무기",System.Threading.Interlocked.Increment(ref headlessWeapons));return;}
			lock (weaponCount_Label)
			{
				UpdateLabel($"{Int32.Parse(weaponCount_Label.Text) + 1}", weaponCount_Label);
			}
		}

		public static void IncrementCharacterCount()
		{
 if(CatheryneScanning.ScanBridge.Active){CatheryneScanning.ScanBridge.Write("running","캐릭터",System.Threading.Interlocked.Increment(ref headlessCharacters));return;}
			lock (characterCount_Label)
			{
				UpdateLabel($"{Int32.Parse(characterCount_Label.Text) + 1}", characterCount_Label);
			}
		}

		public static void SetProgramStatus(string status, bool ok = true)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			MethodInvoker statusAction = delegate
			{
				programStatus_Label.Text = status;
				programStatus_Label.ForeColor = ok ? Color.Green : Color.Red;
				programStatus_Label.Font = new Font(programStatus_Label.Font.FontFamily, 15);
				programStatus_Label.Refresh();
			};

			programStatus_Label.Invoke(statusAction);
		}

		public static void AddError(string error)
		{
 if(CatheryneScanning.ScanBridge.Active){System.Threading.Interlocked.Increment(ref HeadlessErrors);Logger.Error(error);return;}
			UpdateTextBox($"{error.Replace("\n", Environment.NewLine)}" + Environment.NewLine, error_TextBox);
			Logger.Error(error);
		}

		public static void SetNavigation_Image(Bitmap bm)
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			UpdatePictureBox(bm, navigation_PictureBox);
		}

		public static void ResetCharacterDisplay()
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			MethodInvoker nameAction = delegate { cName_PictureBox.Image = null; };
			MethodInvoker levelAction = delegate { cLevel_PictureBox.Image = null; };
			MethodInvoker talentAction_1 = delegate { cTalent_PictureBoxes[0].Image = null; };
			MethodInvoker talentAction_2 = delegate { cTalent_PictureBoxes[1].Image = null; };
			MethodInvoker talentAction_3 = delegate { cTalent_PictureBoxes[2].Image = null; };
			MethodInvoker textAction = delegate { character_TextBox.Clear(); };

			cName_PictureBox.Invoke(nameAction);
			cLevel_PictureBox.Invoke(levelAction);
			cTalent_PictureBoxes[0].Invoke(talentAction_1);
			cTalent_PictureBoxes[1].Invoke(talentAction_2);
			cTalent_PictureBoxes[2].Invoke(talentAction_3);
			character_TextBox.Invoke(textAction);
		}

		public static void ResetGearDisplay()
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			MethodInvoker gearAction = delegate { gear_PictureBox.Image = null; };

			MethodInvoker textAction = delegate { gear_TextBox.Clear(); };

			gear_PictureBox.Invoke(gearAction);
			gear_TextBox.Invoke(textAction);
		}

		public static void ResetCounters()
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			MethodInvoker characterCountAction = delegate { characterCount_Label.Text = "0"; weaponMax_Label.Refresh(); };
			MethodInvoker weaponCountAction = delegate { weaponCount_Label.Text = "0"; weaponMax_Label.Refresh(); };
			MethodInvoker weaponMaxAction = delegate { weaponMax_Label.Text = "?"; weaponMax_Label.Refresh(); };
			MethodInvoker artifactCountAction = delegate { artifactCount_Label.Text = "0"; artifactMax_Label.Refresh(); };
			MethodInvoker artifactMaxAction = delegate { artifactMax_Label.Text = "?"; artifactMax_Label.Refresh(); };

			characterCount_Label.Invoke(characterCountAction);
			weaponCount_Label.Invoke(weaponCountAction);
			weaponMax_Label.Invoke(weaponMaxAction);
			artifactCount_Label.Invoke(artifactCountAction);
			artifactMax_Label.Invoke(artifactMaxAction);
		}

		public static void ResetErrors()
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			MethodInvoker textAction = delegate { error_TextBox.Clear(); };

			error_TextBox.Invoke(textAction);
		}

		public static void ResetAll()
		{
 if(CatheryneScanning.ScanBridge.Active){return;}
			ResetGearDisplay();

			ResetCharacterDisplay();

			ResetCounters();

			ResetErrors();
		}
	}
}