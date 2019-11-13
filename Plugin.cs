using BepInEx;
using BepInEx.Configuration;
using LocalizationCustomSystem;
using MonoMod.RuntimeDetour;
using Sunshine;
using Sunshine.Metric;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Voidforge;

namespace AdvancedCharacterCreation
{
    [BepInPlugin("disco.castar.AdvancedCharacterCreation", "AdvancedCharacterCreation", "0.1")]
    public class Plugin : BaseUnityPlugin
    { 

        private static int _startingAbilityPoints;

        private static int _startingSkillPointsPerAbilityPoint;

        private static AbilityLimitationSetting _abilityLimitSetting;

        private static int _maxSkillValue;

        private static int StartingSkillPoints
        {
            get
            {
                return _startingAbilityPoints * _startingSkillPointsPerAbilityPoint;
            }
        }

        public void Awake()
        {
            ParseConfig(this.Config);

            On.CharacterSheetInfoPanel.ShowSkill += CharacterSheetInfoPanel_ShowSkill;
            On.CharsheetView.SetActiveElements += CharsheetView_SetActiveElements;
            On.XPPanel.UpdateData += XPPanel_UpdateData;
            On.Sunshine.Metric.CharacterSheet.MakeSkills += CharacterSheet_MakeSkills;
            On.CharsheetView.InitCharacter += CharsheetView_InitCharacter;
            On.LevelingUtils.UpgradeSkill += LevelingUtils_UpgradeSkill;
            On.LevelingUtils.IsSkillUpgradeable += LevelingUtils_IsSkillUpgradeable;
            On.LevelingUtils.GetWhySkillCantBeUpgraded += LevelingUtils_GetWhySkillCantBeUpgraded;
            On.Sunshine.Metric.Modifiable.Recalc += Modifiable_Recalc;
            On.SkillPortraitPanel.UpdateWithSkillObject += SkillPortraitPanel_UpdateWithSkillObject;
            On.CharsheetView.RevertAbilities += CharsheetView_RevertAbilities;
            On.SetSignatureButton.Awake += SetSignatureButton_Awake;
            On.SetSignatureButton.GetTooltipData += SetSignatureButton_GetTooltipData;
            On.CharsheetView.LevelingComplete += CharsheetView_LevelingComplete;

            On.CheckNodeUtil.GetCheck += CheckNodeUtil_GetCheck;
            On.PassiveNode.GetCheckInfo_DialogueEntry += PassiveNode_GetCheckInfo_DialogueEntry;
            On.CheckAdvisor.SetTooltipContent += CheckAdvisor_SetTooltipContent;

            On.Sunshine.Metric.CharacterEffect.Apply += CharacterEffect_Apply;
            On.Sunshine.Metric.CharacterEffect.Remove += CharacterEffect_Remove;
        }

        private void ParseConfig(ConfigFile configFile)
        {

            // Settings
            var startingSkillPointsPerAbilityPointConfig = new ConfigDefinition("main", "StartingSkillPointsPerAbilityPoint");
            var startingAbilityPointsConfig = new ConfigDefinition("main", "StartingAbilityPoints");
            var maxSkillValueAtCreation = new ConfigDefinition("main", "MaximumSkillValueAtCreation");
            var skillPointsAbilityLimit = new ConfigDefinition("main", "SkillPointsAbilityLimited");

            bool reload = false;

            if (configFile.GetSetting<int>(startingAbilityPointsConfig) == null)
            {
                configFile.AddSetting<int>(startingAbilityPointsConfig, 8, new ConfigDescription("The amount of Ability points you get at character creation. Default is 8."));
                configFile.Save();
                reload = true;
            }
            if (configFile.GetSetting<int>(startingSkillPointsPerAbilityPointConfig) == null)
            {
                configFile.AddSetting<int>(startingSkillPointsPerAbilityPointConfig, 5, new ConfigDescription("The amount of Skill points you get at character creation, per starting Ability point.\nFor example: the default value of 5, combined with the default value of 8 for starting Ability points, results in a total of 40 Skill points to spend at character creation."));
                configFile.Save();
                reload = true;
            }

            if (configFile.GetSetting<int>(maxSkillValueAtCreation) == null)
            {
                configFile.AddSetting<int>(maxSkillValueAtCreation, 6, new ConfigDescription("The maximum value of a Skill at character creation, regardless of Ability scores or total Skill points."));
                configFile.Save();
                reload = true;
            }

            if (configFile.GetSetting<int>(skillPointsAbilityLimit) == null)
            {
                configFile.AddSetting(skillPointsAbilityLimit, 1, new ConfigDescription("The manner in which skill point assignment is limited by Ability scores.\n1: For a given Ability, you can put skill points in a maximum number of skills tied to that Ability equal to the Ability's score (e.g. if you have Motorics 2, you can increase a maximum of 2 different Motorics skills).\n2: For a given Ability, you can distribute a maximum of StartingSkillPointsPerAbilityPoint times the Ability's value into Skills tied to that ability (e.g. if StartingSkillPointsPerAbilityPoint = 5 (default), and you have Motorics 2, you can distribute a total of 10 skill points in Motorics skills)\n0: No limit on skill points distribution based on Ability scores (not recommended).", new AcceptableValueRange<int>(0, 2)));
                configFile.Save();
                reload = true;
            }

            if (reload)
            {
                configFile.Reload();
            }

            _startingAbilityPoints = configFile.GetSetting<int>(startingAbilityPointsConfig).Value;
            _startingSkillPointsPerAbilityPoint = configFile.GetSetting<int>(startingSkillPointsPerAbilityPointConfig).Value;
            _maxSkillValue = configFile.GetSetting<int>(maxSkillValueAtCreation).Value;
            try
            {
                _abilityLimitSetting = (AbilityLimitationSetting)configFile.GetSetting<int>(skillPointsAbilityLimit).Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ParseConfig: error parsing ability limit setting. Defaulting to 1.");
                _abilityLimitSetting = AbilityLimitationSetting.LIMIT_NR_OF_SKILLS;
            }

            Console.WriteLine("Advanced Character Creation Configuration:");
            Console.WriteLine($". Ability points at character creation: {_startingAbilityPoints}");
            Console.WriteLine($". Skill points per ability point at character creation: {_startingSkillPointsPerAbilityPoint}");
            Console.WriteLine($". => Total nr. of Skill points to spend on character creation: {_startingAbilityPoints * _startingSkillPointsPerAbilityPoint}");
            Console.WriteLine($". Maximum value of any Skill at character creation: {_maxSkillValue}");
            Console.WriteLine($". Ability score limitation on Skill point spending: {Enum.GetName(typeof(AbilityLimitationSetting), _abilityLimitSetting)}");
        }

        private void CharacterEffect_Remove(On.Sunshine.Metric.CharacterEffect.orig_Remove orig, CharacterEffect self, CharacterSheet ch, ModifierType type, IModifierCause modifierCause)
        {
            var modifier = self.GetModifier(type, modifierCause);
            if (self.effect == EffectType.STAT_BONUS)
            {
                for (int i = 0; i < ch.skills.Length; i++)
                {
                    if (ch.skills[i].abilityType == self.abilityType)
                    {
                        ch.skills[i].Remove(modifier);
                    }
                }
            }

            orig(self, ch, type, modifierCause);
        }

        private bool CharacterEffect_Apply(On.Sunshine.Metric.CharacterEffect.orig_Apply orig, CharacterEffect self, CharacterSheet ch, ModifierType type, IModifierCause modifierCause)
        {
            var modifier = self.GetModifier(type, modifierCause);
            if (self.effect == EffectType.STAT_BONUS)
            {
                for (int i = 0; i < ch.skills.Length; i++)
                {
                    if (ch.skills[i].abilityType == self.abilityType)
                    {
                        ch.skills[i].Add(modifier);
                    }
                }
            }

            return orig(self, ch, type, modifierCause);
        }

        private void CharsheetView_LevelingComplete(On.CharsheetView.orig_LevelingComplete orig, CharsheetView self)
        {
            if (self.sheetMode == CharSheetMode.CREATION_SKILLS)
            {
                var hasSavePointProperty = self.GetType().GetProperty("HasSavepoint");
                hasSavePointProperty.GetSetMethod(true).Invoke(self, new object[] { false });
            }
            orig(self);
            if (self.sheetMode == CharSheetMode.CREATION_SKILLS)
            {
                self.NotifyActivation();
            }
        }

        private void CheckAdvisor_SetTooltipContent(On.CheckAdvisor.orig_SetTooltipContent orig, CheckAdvisor self, TooltipSource tooltipSource)
        {
            orig(self, tooltipSource);
            var skillTypeLabel = (Text)self.GetType().GetField("skillTypeLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
            var checkResult = tooltipSource.Data.checkResult;
            var skillBonus = SingletonComponent<World>.Singleton.you.GetSkillBonus(checkResult.skillType);
            skillTypeLabel.text = string.Format("{0}: {1}", checkResult.SkillName().ToUpper(), skillBonus + checkResult.CalcCheckBonus());
        }

        private CheckResult PassiveNode_GetCheckInfo_DialogueEntry(On.PassiveNode.orig_GetCheckInfo_DialogueEntry orig, PixelCrushers.DialogueSystem.DialogueEntry entry)
        {
            var checkResult = orig(entry);
            checkResult.abilityBase = 0;
            return checkResult;
        }

        private CheckResult CheckNodeUtil_GetCheck(On.CheckNodeUtil.orig_GetCheck orig, PixelCrushers.DialogueSystem.DialogueEntry entry, CheckType checkType, string difficultyFieldName)
        {
            var checkResult = orig(entry, checkType, difficultyFieldName);
            checkResult.abilityBase = 0;
            return checkResult;
        }

        private string LevelingUtils_GetWhySkillCantBeUpgraded(On.LevelingUtils.orig_GetWhySkillCantBeUpgraded orig, Skill skill)
        {
            if (SingletonComponent<CharsheetView>.Singleton.sheetMode == CharSheetMode.CREATION_SKILLS)
            {
                var character = SingletonComponent<CharsheetView>.Singleton.character;

                if (LiteSingleton<PlayerCharacter>.Singleton.SkillPoints < 1)
                {
                    return "No Skill points remaining.";
                }

                var modifier = skill.GetModifierOfType(ModifierType.CHARACTER_CREATION);

                if (modifier.Amount >= _maxSkillValue)
                {
                    return $"Cannot increase Skill to more than {_maxSkillValue} on character creation";
                }

                if (_abilityLimitSetting == AbilityLimitationSetting.LIMIT_NR_OF_SKILLS)
                {
                    return "For a given Ability, you can only increase a number of Skills associated with that Ability equal to the Ability's value.";
                }

                if (_abilityLimitSetting == AbilityLimitationSetting.LIMIT_NR_OF_SKILL_POINTS)
                {
                    int maxNrOfSkillPointsSpentPerAbility = (StartingSkillPoints - 1) / _startingAbilityPoints + 1;
                    return $"For a given Ability, you can only spend a total number of Skill points on Skills associated with that Ability equal to the Ability's value times {maxNrOfSkillPointsSpentPerAbility}.";
                }

                return "Unknown reason";
            }
            else
            {
                return orig(skill);
            }
        }

        private bool LevelingUtils_IsSkillUpgradeable(On.LevelingUtils.orig_IsSkillUpgradeable orig, Skill skill)
        {
            if (SingletonComponent<CharsheetView>.Singleton.sheetMode == CharSheetMode.CREATION_SKILLS)
            {
                var modifier = skill.GetModifierOfType(ModifierType.CHARACTER_CREATION);
                if (modifier == null || modifier.Amount >= _maxSkillValue || LiteSingleton<PlayerCharacter>.Singleton.SkillPoints < 1)
                {
                    return false;
                }

                if (_abilityLimitSetting == AbilityLimitationSetting.LIMIT_NR_OF_SKILLS)
                {
                    return modifier.Amount > 1 || !NrOfSkillsLimitReached(skill);
                }
                else if (_abilityLimitSetting == AbilityLimitationSetting.LIMIT_NR_OF_SKILL_POINTS)
                {
                    return !NrOfSkillPointsLimitReached(skill);
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return orig(skill);
            }
        }

        private bool NrOfSkillsLimitReached(Skill skill)
        {
            CharacterSheet character = SingletonComponent<CharsheetView>.Singleton.character;
            var abilityValue = character.GetAbilityValue(Skill.GetAbility(skill.skillType));
            var nrOfIncreasedSkillsForAbility = 0;
            for (int i = 0; i < character.skills.Length; i++)
            {
                if (Skill.GetAbility(skill.skillType) == Skill.GetAbility(character.skills[i].skillType))
                {
                    var modifier = character.skills[i].GetModifierOfType(ModifierType.CHARACTER_CREATION);
                    if (modifier != null && modifier.Amount > 1)
                    {
                        nrOfIncreasedSkillsForAbility++;
                    }
                }
            }

            return nrOfIncreasedSkillsForAbility >= abilityValue;
        }

        private bool NrOfSkillPointsLimitReached(Skill skill)
        {
            CharacterSheet character = SingletonComponent<CharsheetView>.Singleton.character;
            var abilityValue = character.GetAbilityValue(Skill.GetAbility(skill.skillType));
            var nrOfSkillPointsSpentForAbility = 0;
            int maxNrOfSkillPointsSpentForAbility = abilityValue * ((StartingSkillPoints - 1) / _startingAbilityPoints + 1);
            for (int i = 0; i < character.skills.Length; i++)
            {
                if (Skill.GetAbility(skill.skillType) == Skill.GetAbility(character.skills[i].skillType))
                {
                    var modifier = character.skills[i].GetModifierOfType(ModifierType.CHARACTER_CREATION);
                    if (modifier != null && modifier.Amount > 1)
                    {
                        nrOfSkillPointsSpentForAbility += (modifier.Amount-1);
                    }
                }
            }

            return nrOfSkillPointsSpentForAbility >= maxNrOfSkillPointsSpentForAbility;
        }

        private GenericTooltipData SetSignatureButton_GetTooltipData(On.SetSignatureButton.orig_GetTooltipData orig, SetSignatureButton self)
        {
            var skillType = LiteSingleton<CharacterSheetInfoPanel>.Singleton.SelectedSkill.GetSkillType();
            var modifier = GetCharacterCreationModifierForSkillType(skillType);
            if (modifier != null)
            {
                if (modifier.Amount > 1)
                {
                    return new GenericTooltipData
                    {
                        Title = "Decrease " + Skill.SkillTypeToLocalizedName(skillType),
                        Description = "Click to lower " + Skill.SkillTypeToLocalizedName(skillType) + "'s base value by 1",
                        NumericInfo = ""
                    };
                }
                else
                {
                    return new GenericTooltipData
                    {
                        Title = "Cannot decrease " + Skill.SkillTypeToLocalizedName(skillType),
                        Description = "Cannot lower " + Skill.SkillTypeToLocalizedName(skillType) + "'s base value below 1",
                        NumericInfo = ""
                    };
                }
            }
            else
            {
                return orig(self);
            }
        }

        private void SetSignatureButton_Awake(On.SetSignatureButton.orig_Awake orig, SetSignatureButton self)
        {
            var buttonField = self.GetType().GetField("button", BindingFlags.NonPublic | BindingFlags.Instance);
            buttonField.SetValue(self, self.GetComponent<Button>());
            var button = (Button)buttonField.GetValue(self);

            button.onClick.AddListener(delegate ()
            {
                if (self.gameObject.activeInHierarchy && button.interactable && LiteSingleton<CharacterSheetInfoPanel>.Singleton.SelectedSkill != null)
                {
                    var modifier = GetCharacterCreationModifierForSkillType(LiteSingleton<CharacterSheetInfoPanel>.Singleton.SelectedSkill.GetSkillType());
                    if (modifier != null && modifier.Amount > 1)
                    {
                        modifier.Amount--;
                        LiteSingleton<PlayerCharacter>.Singleton.SkillPoints++;
                        SingletonComponent<CharsheetView>.Singleton.character.Recalc();
                        SingletonComponent<CharsheetView>.Singleton.NotifyActivation();
                    }

                }
            });
        }

        private void CharsheetView_RevertAbilities(On.CharsheetView.orig_RevertAbilities orig, CharsheetView self)
        {
            for (int i = 0; i < self.character.skills.Length; i++)
            {
                var modifier = self.character.skills[i].GetModifierOfType(ModifierType.CHARACTER_CREATION);
                if (modifier != null)
                {
                    modifier.Amount = 1;
                }
            }
            LiteSingleton<PlayerCharacter>.Singleton.SkillPoints = StartingSkillPoints;
            orig(self);
        }

        private void SkillPortraitPanel_UpdateWithSkillObject(On.SkillPortraitPanel.orig_UpdateWithSkillObject orig, SkillPortraitPanel self, Skill skillObject)
        {
            var character = SingletonComponent<CharsheetView>.Singleton.character;
            var oldCalculatedAbility = skillObject.calculatedAbility;
            if (character != null) {
                skillObject.calculatedAbility = character.GetAbilityValue(skillObject.abilityType);
            }
            orig(self, skillObject);
            skillObject.calculatedAbility = oldCalculatedAbility;
        }

        private void Modifiable_Recalc(On.Sunshine.Metric.Modifiable.orig_Recalc orig, Modifiable self, CharacterSheet ch)
        {
            orig(self, ch);
            if (self is Skill)
            {
                int actualRankValue = 0;
                bool hasActualAdvancement = false;
                foreach(var modifier in self.GetModifierList())
                {
                    if (modifier.type == ModifierType.ADVANCEMENT)
                    {
                        actualRankValue += modifier.Amount;
                        hasActualAdvancement = true;
                    }
                }
                self.rankValue = actualRankValue;
                self.hasAdvancement = hasActualAdvancement;
            }
        }

        private bool LevelingUtils_UpgradeSkill(On.LevelingUtils.orig_UpgradeSkill orig, CharacterSheet character, SkillType skill)
        {
            if (SingletonComponent<CharsheetView>.Singleton.sheetMode == CharSheetMode.CREATION_SKILLS)
            {
                var modifier = character.GetSkill(skill).GetModifierOfType(ModifierType.CHARACTER_CREATION);
                if (LevelingUtils.IsSkillUpgradeable(character.GetSkill(skill)))
                {
                    modifier.Amount++;
                    LiteSingleton<PlayerCharacter>.Singleton.SkillPoints--;
                    character.Recalc();
                    SingletonComponent<CharsheetView>.Singleton.NotifyActivation();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return orig(character, skill);
            }
        }

        private void CharsheetView_InitCharacter(On.CharsheetView.orig_InitCharacter orig, CharsheetView self, bool forceInit)
        {
            if (_startingAbilityPoints == 8)
            {
                orig(self, forceInit);
            }
            else
            {
                if (!self.IsNewCharInitialized || forceInit)
                {
                    self.character = CharacterSheetFactory.GetOrCreateComponent(SingletonComponent<World>.Singleton.you.gameObject);
                    self.character.Clean();
                    self.unusedAbilityPool = _startingAbilityPoints;
                    self.usedAbilityPoints = 0;
                    self.usedSkillPoints = 0;
                    if (self.abilityMethod == AbilityMethod.ROLL)
                    {
                        int allowedTotal = self.unusedAbilityPool + 4;
                        CharacterSheetFactory.RollAbilities(self.character, allowedTotal);
                        self.usedAbilityPoints = self.unusedAbilityPool;
                        self.unusedAbilityPool = 0;
                    }
                    else if (self.abilityMethod == AbilityMethod.POINTBUY)
                    {
                        CharacterSheetFactory.CleanAbilities(self.character);
                    }
                    else
                    {
                        Debug.LogError("We only roll for now");
                    }
                    SkillPortraitPanel.signatureSkill = SkillType.NONE;
                    self.character.Recalc();
                    self.NotifyActivation();
                    self.GetType().GetProperty("IsNewCharInitialized").GetSetMethod(true).Invoke(self, new object[] { true });
                }
            }

            LiteSingleton<PlayerCharacter>.Singleton.SkillPoints = StartingSkillPoints;
        }

        private void CharacterSheet_MakeSkills(On.Sunshine.Metric.CharacterSheet.orig_MakeSkills orig, CharacterSheet self)
        {
            foreach(Skill skill in self.skills)
            {
                skill.Add(new Modifier(ModifierType.CHARACTER_CREATION, 1,  "Character Creation", null, skill.skillType));
            }
        }

        private void XPPanel_UpdateData(On.XPPanel.orig_UpdateData orig, XPPanel self)
        {
            self.SetValue(LiteSingleton<PlayerCharacter>.Singleton.TotalXpAmount);
            SkillPointField skillPointField = (SkillPointField)self.GetType().GetField("skillPointField", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
            if (skillPointField != null)
            {
                skillPointField.UpdateData();
                return;
            }
        }

        private void CharsheetView_SetActiveElements(On.CharsheetView.orig_SetActiveElements orig, CharsheetView self)
        {
            orig(self);
            if (self.sheetMode == CharSheetMode.CREATION_SKILLS)
            {
                self.xpPanel.gameObject.SetActive(true);
                self.skillPointField.gameObject.SetActive(true);
                self.doneButton.gameObject.SetActive(LiteSingleton<PlayerCharacter>.Singleton.SkillPoints < 1 || NoSkillUpgradable());
            }
        }

        private bool NoSkillUpgradable()
        {
            CharacterSheet character = SingletonComponent<CharsheetView>.Singleton.character;
            for (int i = 0; i < character.skills.Length; i++)
            {
                if (LevelingUtils.IsSkillUpgradeable(character.skills[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void CharacterSheetInfoPanel_ShowSkill(On.CharacterSheetInfoPanel.orig_ShowSkill orig, CharacterSheetInfoPanel self, Sunshine.Metric.Skill skill)
        {
            orig(self, skill);
            if (SingletonComponent<CharsheetView>.Singleton.sheetMode == CharSheetMode.CREATION_SKILLS)
            {
                var buttonTooltipSource = (TooltipSource)self.GetType().GetField("buttonTooltipSource", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
                var buttonTooltipContent = (ButtonTooltip)self.GetType().GetField("buttonTooltipContent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(self);
                if (LevelingUtils.IsSkillUpgradeable(skill))
                {
                    self.skillLevelUpButton.SetInteractable(true);
                    buttonTooltipSource.enabled = false;
                }
                else
                {
                    self.skillLevelUpButton.SetInteractable(false);
                    buttonTooltipSource.enabled = true;
                    buttonTooltipContent.SetTooltip(CharacterSheetConstants.TOOLTIP_TITLE_LEVELUP_REQ, LevelingUtils.GetWhySkillCantBeUpgraded(skill), "");
                }
                self.levelUpButton.gameObject.SetActive(true);
                if (!self.levelUpButton.enabled)
                {
                    Console.WriteLine("Enabling levelup button");
                    self.levelUpButton.enabled = true;
                }
            }
        }

        public static Modifier GetCharacterCreationModifierForSkillType(SkillType skillType)
        {
            Modifier modifier = null;

            var character = SingletonComponent<CharsheetView>.Singleton.character;
            if (character != null)
            {
                modifier = character.GetSkill(skillType).GetModifierOfType(ModifierType.CHARACTER_CREATION);
            }
            return modifier;
        }
    }

    public enum AbilityLimitationSetting
    {
        NONE = 0,
        LIMIT_NR_OF_SKILLS = 1,
        LIMIT_NR_OF_SKILL_POINTS = 2
    }
}
