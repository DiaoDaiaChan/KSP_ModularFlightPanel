using System;
using System.Collections.Generic;
using KSP.Localization;
using SoftMasking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KSP.UI.Screens.Flight;

public class NavBallBurnVector : MonoBehaviour
{
	public Transform vectorProgr;

	public Transform indicationArrow;

	public NavBall navBall;

	public HelixGauge deltaVGauge;

	public HelixGauge deltaVGaugeRed;

	public Transform StageMarkersParent;

	public TextMeshProUGUI ebtText;

	public TextMeshProUGUI TdnText;

	public TextMeshProUGUI readoutText;

	public TextMeshProUGUI sbtText;

	public TextMeshProUGUI bbPercentText;

	public Button btnWarpTo;

	public Button btnAccept;

	public Button btnDismiss;

	public Button btnPercentMinus;

	public Button btnPercentPlus;

	public UIPanelTransitionToggle[] navBallCollapseGroups;

	public double dVremaining;

	public double estimatedBurnTime;

	public double startBurnTime;

	public float accuracy;

	private Vector3 direction;

	private PatchedConicSolver solver;

	[SerializeField]
	private GameObject stageMarkerPrefab;

	[SerializeField]
	private RectTransform stageMarkerMaskRect;

	private DictionaryValueList<int, RotationalGaugeOffsetMarker> stageMarkers;

	[SerializeField]
	private bool enoughDeltaV;

	[SerializeField]
	private int burnBeforePercent;

	[SerializeField]
	private GameObject burnBeforeObject;

	private static double epsilon = 1E-10;

	private bool nodeDeltaVChanged;

	private double nodeDeltaV;

	private bool vesselDeltaVChanged;

	private double vesselTotalDeltaV;

	[SerializeField]
	private List<int> stagesProcessed;

	[SerializeField]
	private Transform EBTextPosBasicMode;

	[SerializeField]
	private Transform EBTextPosExtMode;

	[SerializeField]
	private Transform TdnTextPosBasicMode;

	[SerializeField]
	private Transform TdnTextPosExtMode;

	[SerializeField]
	private double startBurn;

	private static string cacheAutoLOC_258912;

	private static string cacheAutoLOC_460852;

	private void Awake()
	{
		GameEvents.onVesselChange.Add(OnVesselSwitch);
		GameEvents.OnGameSettingsApplied.Add(onGameSettingsApplied);
		btnPercentMinus.onClick.AddListener(btnPercentMinusClick);
		btnPercentPlus.onClick.AddListener(btnPercentPlusClick);
		stageMarkers = new DictionaryValueList<int, RotationalGaugeOffsetMarker>();
		stagesProcessed = new List<int>();
		burnBeforePercent = (int)Math.Round((double)(GameSettings.DELTAV_BURN_PERCENTAGE * 100f) / 10.0) * 10;
		bbPercentText.text = Localizer.Format("#autoLOC_8002209", burnBeforePercent);
	}

	private void OnDestroy()
	{
		GameEvents.onVesselChange.Remove(OnVesselSwitch);
		GameEvents.OnGameSettingsApplied.Remove(onGameSettingsApplied);
		btnPercentMinus.onClick.RemoveListener(btnPercentMinusClick);
		btnPercentPlus.onClick.RemoveListener(btnPercentPlusClick);
	}

	private void Start()
	{
		vectorProgr.gameObject.SetActive(value: false);
		ebtText.enabled = false;
		TdnText.enabled = false;
		sbtText.enabled = false;
		deltaVGauge.gameObject.SetActive(value: false);
		deltaVGaugeRed.gameObject.SetActive(value: false);
		indicationArrow.gameObject.SetActive(value: false);
		burnBeforeObject.SetActive(value: false);
		btnWarpTo.onClick.AddListener(OnWarpToButton);
		btnAccept.onClick.AddListener(OnDismissButton);
		btnDismiss.onClick.AddListener(OnDismissButton);
		btnDismiss.interactable = true;
		nodeDeltaVChanged = true;
		nodeDeltaV = double.MaxValue;
	}

	private void OnVesselSwitch(Vessel v)
	{
		solver = v.patchedConicSolver;
		ClearStageMarkers();
	}

	private void onGameSettingsApplied()
	{
		if (!(solver != null))
		{
			return;
		}
		while (true)
		{
			switch (4)
			{
			case 0:
				continue;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			if (solver.maneuverNodes.Count == 0)
			{
				return;
			}
			while (true)
			{
				switch (7)
				{
				case 0:
					continue;
				}
				if (!navBallIsExpanded())
				{
					return;
				}
				while (true)
				{
					switch (7)
					{
					case 0:
						continue;
					}
					sbtText.enabled = GameSettings.EXTENDED_BURNTIME;
					burnBeforeObject.SetActive(GameSettings.EXTENDED_BURNTIME);
					btnPercentMinus.interactable = GameSettings.EXTENDED_BURNTIME;
					btnPercentPlus.interactable = GameSettings.EXTENDED_BURNTIME;
					Transform obj = ebtText.transform;
					Transform parent;
					if (!GameSettings.EXTENDED_BURNTIME)
					{
						while (true)
						{
							switch (6)
							{
							case 0:
								continue;
							}
							break;
						}
						parent = EBTextPosBasicMode;
					}
					else
					{
						parent = EBTextPosExtMode;
					}
					obj.SetParent(parent);
					ebtText.transform.localPosition = Vector3.zero;
					Transform obj2 = TdnText.transform;
					Transform parent2;
					if (!GameSettings.EXTENDED_BURNTIME)
					{
						while (true)
						{
							switch (3)
							{
							case 0:
								continue;
							}
							break;
						}
						parent2 = TdnTextPosBasicMode;
					}
					else
					{
						parent2 = TdnTextPosExtMode;
					}
					obj2.SetParent(parent2);
					TdnText.transform.localPosition = Vector3.zero;
					return;
				}
			}
		}
	}

	public void SetAdvancedMode(bool mode)
	{
		GameSettings.EXTENDED_BURNTIME = mode;
		onGameSettingsApplied();
	}

	private void LateUpdate()
	{
		if (!FlightGlobals.ready)
		{
			while (true)
			{
				switch (2)
				{
				case 0:
					break;
				default:
					if (1 == 0)
					{
						/*OpCode not supported: LdMemberToken*/;
					}
					return;
				}
			}
		}
		if (solver != null)
		{
			while (true)
			{
				switch (3)
				{
				case 0:
					continue;
				}
				break;
			}
			if (solver.maneuverNodes.Count != 0)
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						continue;
					}
					break;
				}
				if (navBallIsExpanded())
				{
					while (true)
					{
						double num;
						double num2;
						Button button;
						int interactable;
						switch (7)
						{
						case 0:
							break;
						default:
							{
								if (!deltaVGauge.gameObject.activeInHierarchy)
								{
									while (true)
									{
										switch (5)
										{
										case 0:
											continue;
										}
										break;
									}
									vectorProgr.gameObject.SetActive(value: true);
									deltaVGauge.gameObject.SetActive(value: true);
									ebtText.enabled = true;
									TdnText.enabled = true;
									sbtText.enabled = GameSettings.EXTENDED_BURNTIME;
									burnBeforeObject.SetActive(GameSettings.EXTENDED_BURNTIME);
									Transform obj = ebtText.transform;
									Transform parent;
									if (!GameSettings.EXTENDED_BURNTIME)
									{
										while (true)
										{
											switch (4)
											{
											case 0:
												continue;
											}
											break;
										}
										parent = EBTextPosBasicMode;
									}
									else
									{
										parent = EBTextPosExtMode;
									}
									obj.SetParent(parent);
									ebtText.transform.localPosition = Vector3.zero;
									Transform obj2 = TdnText.transform;
									Transform parent2;
									if (!GameSettings.EXTENDED_BURNTIME)
									{
										while (true)
										{
											switch (5)
											{
											case 0:
												continue;
											}
											break;
										}
										parent2 = TdnTextPosBasicMode;
									}
									else
									{
										parent2 = TdnTextPosExtMode;
									}
									obj2.SetParent(parent2);
									TdnText.transform.localPosition = Vector3.zero;
									btnPercentMinus.interactable = GameSettings.EXTENDED_BURNTIME;
									btnPercentPlus.interactable = GameSettings.EXTENDED_BURNTIME;
								}
								direction = solver.maneuverNodes[0].GetBurnVector(solver.maneuverNodes[0].patch);
								dVremaining = direction.magnitude;
								deltaVGauge.currentValue = (float)dVremaining;
								if (Math.Abs(nodeDeltaV - solver.maneuverNodes[0].DeltaV.magnitude) > epsilon)
								{
									while (true)
									{
										switch (6)
										{
										case 0:
											continue;
										}
										break;
									}
									nodeDeltaVChanged = true;
									nodeDeltaV = solver.maneuverNodes[0].DeltaV.magnitude;
									deltaVGauge.MaxValue = (float)nodeDeltaV;
									if (FlightGlobals.ActiveVessel.VesselDeltaV != null)
									{
										while (true)
										{
											switch (5)
											{
											case 0:
												continue;
											}
											break;
										}
										if (FlightGlobals.ActiveVessel.VesselDeltaV.IsReady)
										{
											while (true)
											{
												switch (4)
												{
												case 0:
													continue;
												}
												break;
											}
											for (int i = 0; i < stageMarkers.Count; i++)
											{
												if (stageMarkers.TryGetValue(stageMarkers.KeyAt(i), out var val))
												{
													while (true)
													{
														switch (4)
														{
														case 0:
															continue;
														}
														break;
													}
													val.maxValue = deltaVGauge.MaxValue;
													double value = (double)val.maxValue - ((double)val.maxValue - nodeDeltaV) - val.StageDV;
													val.SetValue(value);
												}
											}
											while (true)
											{
												switch (4)
												{
												case 0:
													continue;
												}
												break;
											}
										}
									}
								}
								if (FlightGlobals.ActiveVessel.ctrlState.mainThrottle > 0f)
								{
									while (true)
									{
										switch (3)
										{
										case 0:
											continue;
										}
										break;
									}
									if (StageManager.CurrentStage <= StageManager.LastStage)
									{
										num = FlightGlobals.ActiveVessel.VesselDeltaV.TotalDeltaVActual;
										goto IL_03f3;
									}
								}
								while (true)
								{
									switch (5)
									{
									case 0:
										continue;
									}
									break;
								}
								if (!(FlightGlobals.ActiveVessel.atmDensity > 0.0))
								{
									while (true)
									{
										switch (3)
										{
										case 0:
											continue;
										}
										break;
									}
									num = FlightGlobals.ActiveVessel.VesselDeltaV.TotalDeltaVVac;
								}
								else
								{
									num = FlightGlobals.ActiveVessel.VesselDeltaV.TotalDeltaVASL;
								}
								goto IL_03f3;
							}
							IL_03f3:
							num2 = num;
							if (FlightGlobals.ActiveVessel != null)
							{
								while (true)
								{
									switch (3)
									{
									case 0:
										continue;
									}
									break;
								}
								if (FlightGlobals.ActiveVessel.VesselDeltaV != null)
								{
									while (true)
									{
										switch (5)
										{
										case 0:
											continue;
										}
										break;
									}
									if (Math.Abs(vesselTotalDeltaV - num2) > epsilon)
									{
										while (true)
										{
											switch (2)
											{
											case 0:
												continue;
											}
											break;
										}
										vesselDeltaVChanged = true;
										vesselTotalDeltaV = num2;
									}
								}
							}
							if (FlightGlobals.ActiveVessel.VesselDeltaV != null)
							{
								while (true)
								{
									switch (1)
									{
									case 0:
										continue;
									}
									break;
								}
								if (!FlightGlobals.ActiveVessel.VesselDeltaV.SimulationRunning)
								{
									while (true)
									{
										switch (5)
										{
										case 0:
											continue;
										}
										break;
									}
									if (FlightGlobals.ActiveVessel.VesselDeltaV.IsReady)
									{
										while (true)
										{
											switch (4)
											{
											case 0:
												continue;
											}
											break;
										}
										estimatedBurnTime = CalculateBurnTime();
										UpdateDVStageMarkers();
									}
								}
							}
							UpdateNavballBurnVectors();
							accuracy = 1f - deltaVGauge.currentValue / deltaVGauge.MaxValue;
							if (accuracy > 0.99f)
							{
								while (true)
								{
									switch (6)
									{
									case 0:
										continue;
									}
									break;
								}
								if (!btnAccept.gameObject.activeSelf)
								{
									while (true)
									{
										switch (5)
										{
										case 0:
											continue;
										}
										break;
									}
									btnAccept.gameObject.SetActive(value: true);
								}
								if (btnDismiss.gameObject.activeSelf)
								{
									while (true)
									{
										switch (6)
										{
										case 0:
											continue;
										}
										break;
									}
									btnDismiss.gameObject.SetActive(value: false);
								}
							}
							else
							{
								if (btnAccept.gameObject.activeSelf)
								{
									while (true)
									{
										switch (7)
										{
										case 0:
											continue;
										}
										break;
									}
									btnAccept.gameObject.SetActive(value: false);
								}
								if (!btnDismiss.gameObject.activeSelf)
								{
									while (true)
									{
										switch (1)
										{
										case 0:
											continue;
										}
										break;
									}
									btnDismiss.gameObject.SetActive(value: true);
								}
							}
							if (!btnDismiss.interactable)
							{
								while (true)
								{
									switch (7)
									{
									case 0:
										continue;
									}
									break;
								}
								btnDismiss.interactable = true;
							}
							UpdateBurnUIText();
							AutoStepNodes();
							button = btnWarpTo;
							if (startBurn > (double)GameSettings.WARP_TO_MANNODE_MARGIN)
							{
								while (true)
								{
									switch (1)
									{
									case 0:
										continue;
									}
									break;
								}
								interactable = ((InputLockManager.GetControlLock("TimeWarpTo") == ControlTypes.None) ? 1 : 0);
							}
							else
							{
								interactable = 0;
							}
							button.interactable = (byte)interactable != 0;
							return;
						}
					}
				}
			}
		}
		if (deltaVGauge.gameObject.activeSelf)
		{
			while (true)
			{
				switch (6)
				{
				case 0:
					continue;
				}
				break;
			}
			vectorProgr.gameObject.SetActive(value: false);
			indicationArrow.gameObject.SetActive(value: false);
			deltaVGauge.gameObject.SetActive(value: false);
			ebtText.enabled = false;
			TdnText.enabled = false;
			sbtText.enabled = false;
			burnBeforeObject.SetActive(value: false);
		}
		if (deltaVGaugeRed.gameObject.activeSelf)
		{
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			deltaVGaugeRed.gameObject.SetActive(value: false);
		}
		ClearStageMarkers();
		startBurn = 0.0;
		nodeDeltaV = double.MaxValue;
	}

	private void UpdateNavballBurnVectors()
	{
		vectorProgr.localPosition = navBall.attitudeGymbal * (direction.normalized * navBall.VectorUnitScale);
		if (direction.magnitude > navBall.VectorVelocityThreshold)
		{
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			if (vectorProgr.transform.localPosition.z >= navBall.VectorUnitCutoff)
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						break;
					default:
						if (!vectorProgr.gameObject.activeSelf)
						{
							while (true)
							{
								switch (6)
								{
								case 0:
									continue;
								}
								break;
							}
							vectorProgr.gameObject.SetActive(value: true);
						}
						if (indicationArrow.gameObject.activeSelf)
						{
							while (true)
							{
								switch (5)
								{
								case 0:
									break;
								default:
									indicationArrow.gameObject.SetActive(value: false);
									return;
								}
							}
						}
						return;
					}
				}
			}
		}
		if (vectorProgr.gameObject.activeSelf)
		{
			while (true)
			{
				switch (7)
				{
				case 0:
					continue;
				}
				break;
			}
			vectorProgr.gameObject.SetActive(value: false);
		}
		if (vectorProgr.gameObject.activeSelf)
		{
			return;
		}
		while (true)
		{
			switch (5)
			{
			case 0:
				continue;
			}
			if (!indicationArrow.gameObject.activeSelf)
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						continue;
					}
					break;
				}
				indicationArrow.gameObject.SetActive(value: true);
			}
			Vector3 localPosition = vectorProgr.localPosition;
			Vector3 localPosition2 = localPosition - Vector3.Dot(localPosition, Vector3.forward) * Vector3.forward;
			localPosition2.Normalize();
			localPosition2 *= navBall.VectorUnitScale * 0.6f;
			indicationArrow.localPosition = localPosition2;
			float num = 57.29578f * Mathf.Acos(localPosition2.x / Mathf.Sqrt(localPosition2.x * localPosition2.x + localPosition2.y * localPosition2.y));
			if (localPosition2.y < 0f)
			{
				while (true)
				{
					switch (7)
					{
					case 0:
						continue;
					}
					break;
				}
				num += 2f * (180f - num);
			}
			if (float.IsNaN(num))
			{
				while (true)
				{
					switch (7)
					{
					case 0:
						continue;
					}
					break;
				}
				num = 0f;
			}
			Quaternion localRotation = Quaternion.Euler(num + 90f, 270f, 90f);
			indicationArrow.localRotation = localRotation;
			return;
		}
	}

	private void UpdateBurnUIText()
	{
		string text = cacheAutoLOC_258912;
		readoutText.text = Localizer.Format("#autoLOC_460807", dVremaining.ToString("0.0"));
		ebtText.text = "<color=#98EE00>";
		if (GameSettings.DELTAV_BURN_ESTIMATE_COLORS)
		{
			while (true)
			{
				switch (2)
				{
				case 0:
					continue;
				}
				break;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			if (!enoughDeltaV)
			{
				while (true)
				{
					switch (2)
					{
					case 0:
						continue;
					}
					break;
				}
				ebtText.text = "<color=#FF0000>";
			}
		}
		string text2 = "";
		if (double.IsInfinity(estimatedBurnTime))
		{
			while (true)
			{
				switch (3)
				{
				case 0:
					continue;
				}
				break;
			}
			text2 = text;
		}
		else if (estimatedBurnTime > 1.0)
		{
			while (true)
			{
				switch (2)
				{
				case 0:
					continue;
				}
				break;
			}
			text2 = KSPUtil.PrintTime(estimatedBurnTime, 2, explicitPositive: false);
		}
		else if (estimatedBurnTime < 1.0)
		{
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			if (estimatedBurnTime > 0.0)
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						continue;
					}
					break;
				}
				if (estimatedBurnTime < 0.10000000149011612)
				{
					while (true)
					{
						switch (1)
						{
						case 0:
							continue;
						}
						break;
					}
					text2 = "0.1" + Localizer.Format("#autoLOC_6002317");
				}
				else
				{
					text2 = estimatedBurnTime.ToString("0.0") + Localizer.Format("#autoLOC_6002317");
				}
			}
		}
		ebtText.text += Localizer.Format("#autoLOC_460808", text2);
		ebtText.text += "</color>";
		TdnText.text = Localizer.Format("#autoLOC_460809", KSPUtil.PrintTime(Planetarium.GetUniversalTime() - solver.maneuverNodes[0].UT, 3, explicitPositive: true));
		startBurn = 0.0;
		if (burnBeforePercent == 0)
		{
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			startBurn = solver.maneuverNodes[0].UT - Planetarium.GetUniversalTime();
		}
		else if (burnBeforePercent == 100)
		{
			while (true)
			{
				switch (6)
				{
				case 0:
					continue;
				}
				break;
			}
			startBurn = solver.maneuverNodes[0].UT - Planetarium.GetUniversalTime() - estimatedBurnTime;
		}
		else
		{
			startBurn = solver.maneuverNodes[0].UT - Planetarium.GetUniversalTime() - startBurnTime;
		}
		solver.maneuverNodes[0].startBurnIn = startBurn;
		if (GameSettings.DELTAV_BURN_TIME_COLORS)
		{
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			if (startBurn <= 0.0)
			{
				while (true)
				{
					switch (2)
					{
					case 0:
						continue;
					}
					break;
				}
				if (FlightGlobals.ActiveVessel.ctrlState.mainThrottle <= 0f)
				{
					while (true)
					{
						switch (4)
						{
						case 0:
							continue;
						}
						break;
					}
					sbtText.text = "<color=#FF0000>";
					goto IL_03ab;
				}
			}
			if (startBurn > 0.0)
			{
				while (true)
				{
					switch (4)
					{
					case 0:
						continue;
					}
					break;
				}
				if (startBurn < 10.0)
				{
					while (true)
					{
						switch (4)
						{
						case 0:
							continue;
						}
						break;
					}
					sbtText.text = "<color=#FF9600>";
					goto IL_03ab;
				}
			}
			sbtText.text = "<color=#98EE00>";
		}
		else
		{
			sbtText.text = "<color=#98EE00>";
		}
		goto IL_03ab;
		IL_03ab:
		TextMeshProUGUI textMeshProUGUI = sbtText;
		textMeshProUGUI.text = textMeshProUGUI.text + Localizer.Format("#autoLOC_8002210", KSPUtil.PrintTime(startBurn, 2, explicitPositive: false)) + "</color>";
	}

	private void UpdateDVStageMarkers()
	{
		VesselDeltaV vesselDeltaV = FlightGlobals.ActiveVessel.VesselDeltaV;
		if (!nodeDeltaVChanged)
		{
			while (true)
			{
				switch (6)
				{
				case 0:
					continue;
				}
				break;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			if (!vesselDeltaVChanged)
			{
				goto IL_0116;
			}
			while (true)
			{
				switch (3)
				{
				case 0:
					continue;
				}
				break;
			}
		}
		if (vesselTotalDeltaV < nodeDeltaV)
		{
			while (true)
			{
				switch (2)
				{
				case 0:
					continue;
				}
				break;
			}
			if (!deltaVGaugeRed.gameObject.activeSelf)
			{
				while (true)
				{
					switch (6)
					{
					case 0:
						continue;
					}
					break;
				}
				deltaVGaugeRed.gameObject.SetActive(value: true);
			}
			deltaVGaugeRed.MaxValue = (float)nodeDeltaV;
			deltaVGaugeRed.currentValue = deltaVGaugeRed.MaxValue - (deltaVGaugeRed.MaxValue - deltaVGauge.currentValue) - (float)vesselTotalDeltaV;
		}
		else if (deltaVGaugeRed.gameObject.activeSelf)
		{
			while (true)
			{
				switch (6)
				{
				case 0:
					continue;
				}
				break;
			}
			deltaVGaugeRed.gameObject.SetActive(value: false);
		}
		nodeDeltaVChanged = false;
		vesselDeltaVChanged = false;
		goto IL_0116;
		IL_0116:
		if (vesselDeltaV.OperatingStageInfo.Count == 0)
		{
			while (true)
			{
				switch (4)
				{
				case 0:
					break;
				default:
					ClearStageMarkers();
					return;
				}
			}
		}
		double num = dVremaining;
		double num2 = 0.0;
		int num3 = 0;
		int num4 = 0;
		stagesProcessed.Clear();
		while (num > 0.0)
		{
			while (true)
			{
				switch (7)
				{
				case 0:
					continue;
				}
				break;
			}
			DeltaVStageInfo deltaVStageInfo;
			float num5;
			if (num3 < vesselDeltaV.OperatingStageInfo.Count)
			{
				deltaVStageInfo = vesselDeltaV.OperatingStageInfo[num3];
				if (FlightGlobals.ActiveVessel.ctrlState.mainThrottle > 0f)
				{
					while (true)
					{
						switch (3)
						{
						case 0:
							continue;
						}
						break;
					}
					if (StageManager.CurrentStage <= deltaVStageInfo.stage)
					{
						num5 = deltaVStageInfo.deltaVActual;
						goto IL_01ee;
					}
				}
				while (true)
				{
					switch (5)
					{
					case 0:
						continue;
					}
					break;
				}
				if (!(FlightGlobals.ActiveVessel.atmDensity > 0.0))
				{
					while (true)
					{
						switch (2)
						{
						case 0:
							continue;
						}
						break;
					}
					num5 = deltaVStageInfo.deltaVinVac;
				}
				else
				{
					num5 = deltaVStageInfo.deltaVatASL;
				}
				goto IL_01ee;
			}
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			break;
			IL_01ee:
			double num6 = num5;
			if (num6 <= 0.0)
			{
				while (true)
				{
					switch (1)
					{
					case 0:
						continue;
					}
					break;
				}
				num3++;
				continue;
			}
			num -= num6;
			if (!stageMarkers.TryGetValue(deltaVStageInfo.stage, out var val))
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						continue;
					}
					break;
				}
				GameObject obj = UnityEngine.Object.Instantiate(stageMarkerPrefab);
				val = obj.GetComponent<RotationalGaugeOffsetMarker>();
				val.transform.SetParent(StageMarkersParent);
				val.transform.localPosition = Vector3.zero;
				val.transform.localScale = Vector3.one;
				val.offsetObject.transform.SetParent(StageMarkersParent);
				val.offsetObject.transform.localPosition = Vector3.zero;
				val.offsetObject.transform.localScale = Vector3.one;
				SoftMask component = obj.GetComponent<SoftMask>();
				if (component != null)
				{
					while (true)
					{
						switch (1)
						{
						case 0:
							continue;
						}
						break;
					}
					component.separateMask = stageMarkerMaskRect;
				}
				stageMarkers.Add(deltaVStageInfo.stage, val);
			}
			val.maxValue = deltaVGauge.MaxValue;
			num2 += num6;
			if (Math.Abs(num2 - val.StageDV) > epsilon)
			{
				while (true)
				{
					switch (7)
					{
					case 0:
						continue;
					}
					break;
				}
				val.StageDV = num2;
				double value = (double)(val.maxValue - (val.maxValue - deltaVGauge.currentValue)) - num2;
				val.SetValue(value);
			}
			if (stagesProcessed.Count > 0)
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						continue;
					}
					break;
				}
				if (stageMarkers.TryGetValue(stagesProcessed[stagesProcessed.Count - 1], out var val2))
				{
					while (true)
					{
						switch (4)
						{
						case 0:
							continue;
						}
						break;
					}
					if (val2 != null)
					{
						while (true)
						{
							switch (2)
							{
							case 0:
								continue;
							}
							break;
						}
						val.SetNextToValue(val2.CurrentAngle);
						if (Mathf.Abs(val.CurrentAngle - val2.CurrentAngle) < 5f)
						{
							while (true)
							{
								switch (1)
								{
								case 0:
									continue;
								}
								break;
							}
							num4++;
						}
						else
						{
							num4 = 0;
						}
					}
				}
			}
			else
			{
				val.SetNextToValue(val.maxRot);
			}
			int opacity = 255;
			if (num4 == 2)
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						continue;
					}
					break;
				}
				opacity = 127;
			}
			else if (num4 == 3)
			{
				while (true)
				{
					switch (5)
					{
					case 0:
						continue;
					}
					break;
				}
				opacity = 63;
			}
			else if (num4 > 3)
			{
				while (true)
				{
					switch (7)
					{
					case 0:
						continue;
					}
					break;
				}
				opacity = 26;
			}
			val.SetTextField(deltaVStageInfo.stage.ToString(), opacity);
			RotationalGaugeOffsetMarker rotationalGaugeOffsetMarker = val;
			int active;
			if (deltaVStageInfo.stage != vesselDeltaV.lowestStageWithDeltaV)
			{
				while (true)
				{
					switch (3)
					{
					case 0:
						continue;
					}
					break;
				}
				active = ((num > 0.0) ? 1 : 0);
			}
			else
			{
				active = 0;
			}
			rotationalGaugeOffsetMarker.ToggleOffsetMarker((byte)active != 0);
			stagesProcessed.Add(deltaVStageInfo.stage);
			num3++;
		}
		RemoveRemainingStageMarkers(stagesProcessed);
	}

	private double CalculateBurnTime()
	{
		VesselDeltaV vesselDeltaV = FlightGlobals.ActiveVessel.VesselDeltaV;
		float num = 0f;
		double num2 = 0.0;
		startBurnTime = 0.0;
		float num3 = 0f;
		enoughDeltaV = false;
		bool flag = false;
		float num4 = 0f;
		if (burnBeforePercent != 0)
		{
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			if (burnBeforePercent != 100)
			{
				num4 = (float)dVremaining * (float)burnBeforePercent / 100f;
				goto IL_008e;
			}
			while (true)
			{
				switch (2)
				{
				case 0:
					continue;
				}
				break;
			}
		}
		flag = true;
		goto IL_008e;
		IL_008e:
		int num5 = 0;
		while (true)
		{
			if (num5 < vesselDeltaV.OperatingStageInfo.Count)
			{
				DeltaVStageInfo deltaVStageInfo = vesselDeltaV.OperatingStageInfo[num5];
				int num6;
				if (FlightGlobals.ActiveVessel.ctrlState.mainThrottle > 0f)
				{
					while (true)
					{
						switch (6)
						{
						case 0:
							continue;
						}
						break;
					}
					num6 = ((StageManager.CurrentStage <= deltaVStageInfo.stage) ? 1 : 0);
				}
				else
				{
					num6 = 0;
				}
				bool flag2 = (byte)num6 != 0;
				float num7;
				if (!flag2)
				{
					while (true)
					{
						switch (5)
						{
						case 0:
							continue;
						}
						break;
					}
					if (!(FlightGlobals.ActiveVessel.atmDensity > 0.0))
					{
						while (true)
						{
							switch (7)
							{
							case 0:
								continue;
							}
							break;
						}
						num7 = deltaVStageInfo.deltaVinVac;
					}
					else
					{
						num7 = deltaVStageInfo.deltaVatASL;
					}
				}
				else
				{
					num7 = deltaVStageInfo.deltaVActual;
				}
				double num8 = num7;
				if (!enoughDeltaV)
				{
					while (true)
					{
						switch (2)
						{
						case 0:
							continue;
						}
						break;
					}
					if ((double)num + num8 > dVremaining)
					{
						while (true)
						{
							switch (7)
							{
							case 0:
								continue;
							}
							break;
						}
						double num9 = deltaVStageInfo.CalculateTimeRequiredDV(flag2, (float)dVremaining - num);
						num2 += num9;
						enoughDeltaV = true;
					}
					else
					{
						num += (float)num8;
						num2 += deltaVStageInfo.stageBurnTime;
					}
				}
				if (!flag)
				{
					while (true)
					{
						switch (7)
						{
						case 0:
							continue;
						}
						break;
					}
					if (num3 + (float)num8 > num4)
					{
						while (true)
						{
							switch (6)
							{
							case 0:
								continue;
							}
							break;
						}
						double num10 = deltaVStageInfo.CalculateTimeRequiredDV(flag2, num4 - num3);
						startBurnTime += num10;
						flag = true;
					}
					else
					{
						num3 += (float)num8;
						startBurnTime += deltaVStageInfo.stageBurnTime;
					}
				}
				if (enoughDeltaV && flag)
				{
					break;
				}
				while (true)
				{
					switch (1)
					{
					case 0:
						break;
					default:
						goto end_IL_01f7;
					}
					continue;
					end_IL_01f7:
					break;
				}
				num5++;
				continue;
			}
			while (true)
			{
				switch (1)
				{
				case 0:
					continue;
				}
				break;
			}
			break;
		}
		return num2;
	}

	private void AutoStepNodes()
	{
		if (solver.maneuverNodes.Count <= 1)
		{
			return;
		}
		while (true)
		{
			switch (1)
			{
			case 0:
				continue;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			if (!(Math.Abs(Planetarium.GetUniversalTime() - solver.maneuverNodes[0].UT) > Math.Abs(Planetarium.GetUniversalTime() - solver.maneuverNodes[1].UT)))
			{
				return;
			}
			while (true)
			{
				switch (2)
				{
				case 0:
					continue;
				}
				solver.maneuverNodes[0].RemoveSelf();
				return;
			}
		}
	}

	private void ClearStageMarkers()
	{
		for (int i = 0; i < stageMarkers.ValuesList.Count; i++)
		{
			stageMarkers.ValuesList[i].gameObject.DestroyGameObject();
		}
		while (true)
		{
			switch (7)
			{
			case 0:
				continue;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			stageMarkers.Clear();
			return;
		}
	}

	private void RemoveRemainingStageMarkers(List<int> stagesProcessed)
	{
		int count = stageMarkers.KeysList.Count;
		while (count-- > 0)
		{
			if (stagesProcessed.Contains(stageMarkers.KeysList[count]))
			{
				continue;
			}
			while (true)
			{
				switch (6)
				{
				case 0:
					continue;
				}
				break;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			RotationalGaugeOffsetMarker rotationalGaugeOffsetMarker = stageMarkers.ValuesList[count];
			stageMarkers.Remove(stageMarkers.KeysList[count]);
			rotationalGaugeOffsetMarker.gameObject.DestroyGameObject();
		}
		while (true)
		{
			switch (5)
			{
			case 0:
				break;
			default:
				return;
			}
		}
	}

	private void btnPercentMinusClick()
	{
		if (burnBeforePercent <= 0)
		{
			return;
		}
		while (true)
		{
			switch (2)
			{
			case 0:
				continue;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			burnBeforePercent -= 10;
			GameSettings.DELTAV_BURN_PERCENTAGE = (float)burnBeforePercent / 100f;
			GameSettings.SaveGameSettingsOnly();
			bbPercentText.text = Localizer.Format("#autoLOC_8002209", burnBeforePercent);
			return;
		}
	}

	private void btnPercentPlusClick()
	{
		if (burnBeforePercent > 90)
		{
			return;
		}
		while (true)
		{
			switch (2)
			{
			case 0:
				continue;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			burnBeforePercent += 10;
			GameSettings.DELTAV_BURN_PERCENTAGE = (float)burnBeforePercent / 100f;
			GameSettings.SaveGameSettingsOnly();
			bbPercentText.text = Localizer.Format("#autoLOC_8002209", burnBeforePercent);
			return;
		}
	}

	protected void OnDismissButton()
	{
		if (InputLockManager.IsUnlocked(ControlTypes.MANNODE_DELETE))
		{
			while (true)
			{
				switch (1)
				{
				case 0:
					break;
				default:
					if (1 == 0)
					{
						/*OpCode not supported: LdMemberToken*/;
					}
					if (InputLockManager.IsLocked(ControlTypes.WARPTO_LOCK))
					{
						while (true)
						{
							switch (1)
							{
							case 0:
								continue;
							}
							break;
						}
						TimeWarp.fetch.CancelAutoWarp(0);
					}
					solver.maneuverNodes[0].RemoveSelf();
					ManeuverNodeEditorManager.Instance.SetManeuverNodeInitialValues();
					return;
				}
			}
		}
		ScreenMessages.PostScreenMessage(cacheAutoLOC_460852, 3f, ScreenMessageStyle.UPPER_CENTER);
	}

	protected void OnWarpToButton()
	{
		if (!(FlightGlobals.ActiveVessel.patchedConicSolver != null))
		{
			return;
		}
		while (true)
		{
			switch (2)
			{
			case 0:
				continue;
			}
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			if (FlightGlobals.ActiveVessel.patchedConicSolver.maneuverNodes.Count <= 0)
			{
				return;
			}
			while (true)
			{
				switch (4)
				{
				case 0:
					continue;
				}
				double uT = FlightGlobals.ActiveVessel.patchedConicSolver.maneuverNodes[0].startBurnIn + Planetarium.GetUniversalTime() - (double)GameSettings.WARP_TO_MANNODE_MARGIN;
				TimeWarp.fetch.WarpTo(uT);
				return;
			}
		}
	}

	private bool navBallIsExpanded()
	{
		int num = navBallCollapseGroups.Length;
		while (num-- > 0)
		{
			if (navBallCollapseGroups[num].expanded)
			{
				continue;
			}
			while (true)
			{
				switch (5)
				{
				case 0:
					continue;
				}
				if (1 == 0)
				{
					/*OpCode not supported: LdMemberToken*/;
				}
				return false;
			}
		}
		while (true)
		{
			switch (2)
			{
			case 0:
				continue;
			}
			return true;
		}
	}

	internal static void CacheLocalStrings()
	{
		cacheAutoLOC_258912 = Localizer.Format("#autoLOC_258912");
		cacheAutoLOC_460852 = Localizer.Format("#autoLOC_460852");
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.1.0.9782' (yours is '8.2.0.7535-95108c96')
