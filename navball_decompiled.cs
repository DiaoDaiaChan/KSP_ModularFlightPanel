using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KSP.UI.Screens.Flight;

public class NavBall : MonoBehaviour
{
	public Transform navBall;

	public Transform progradeVector;

	public Transform retrogradeVector;

	public Transform normalVector;

	public Transform antiNormalVector;

	public Transform radialInVector;

	public Transform radialOutVector;

	public Transform progradeWaypoint;

	public Transform retrogradeWaypoint;

	public TextMeshProUGUI headingText;

	public Image sideGaugeGee;

	public Image sideGaugeThrottle;

	private Vector3 rotationOffset = new Vector3(90f, 0f, 0f);

	private Vector3 displayVelocity;

	private float displaySpeed;

	private Vector3 displayVelDir;

	private bool initialHeadingSet;

	[SerializeField]
	private float vectorUnitScale = 1f;

	[SerializeField]
	private float vectorUnitCutoff = 0.022f;

	[SerializeField]
	private float vectorVelocityThreshold = 0.1f;

	private Vector3 wCoM;

	private Vector3 obtVel;

	private Vector3 cbPos;

	private Vector3 normal;

	private Vector3 radial;

	public Transform target { get; protected set; }

	public Quaternion attitudeGymbal { get; protected set; }

	public Quaternion relativeGymbal { get; protected set; }

	public Quaternion offsetGymbal { get; protected set; }

	[SerializeField]
	public float VectorUnitScale => vectorUnitScale;

	[SerializeField]
	public float VectorUnitCutoff => vectorUnitCutoff;

	[SerializeField]
	public float VectorVelocityThreshold => vectorVelocityThreshold;

	private void Start()
	{
		Texture2D texture = GameDatabase.Instance.GetTexture("Squad/Props/NavBall/GaugeGee", asNormalMap: false);
		if (texture != null)
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
			sideGaugeGee.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
		}
		Texture2D texture2 = GameDatabase.Instance.GetTexture("Squad/Props/NavBall/GaugeThrottle", asNormalMap: false);
		if (!(texture2 != null))
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
			sideGaugeThrottle.sprite = Sprite.Create(texture2, new Rect(0f, 0f, texture2.width, texture2.height), new Vector2(0.5f, 0.5f));
			return;
		}
	}

	private void Update()
	{
		if (!FlightGlobals.ready)
		{
			while (true)
			{
				switch (6)
				{
				case 0:
					continue;
				}
				if (1 == 0)
				{
					/*OpCode not supported: LdMemberToken*/;
				}
				return;
			}
		}
		CelestialBody currentMainBody = FlightGlobals.currentMainBody;
		target = FlightGlobals.ActiveVessel.ReferenceTransform;
		Vector3 euler;
		if (!FlightGlobals.ActiveVessel.isEVA)
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
			euler = rotationOffset;
		}
		else
		{
			euler = new Vector3(0f, 0f, 0f);
		}
		offsetGymbal = Quaternion.Euler(euler);
		if (FlightGlobals.ActiveVessel.isEVA)
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
			if (!MapView.MapIsEnabled)
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
				if (FlightCamera.fetch != null)
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
					attitudeGymbal = Quaternion.Inverse(FlightCamera.fetch.getReferenceFrame() * Quaternion.AngleAxis(FlightCamera.fetch.camHdg * 57.29578f, Vector3.up) * Quaternion.AngleAxis(FlightCamera.fetch.camPitch * 57.29578f, Vector3.right));
					goto IL_0154;
				}
			}
		}
		attitudeGymbal = offsetGymbal * Quaternion.Inverse(target.rotation);
		goto IL_0154;
		IL_0312:
		progradeVector.gameObject.SetActive(value: false);
		retrogradeVector.gameObject.SetActive(value: false);
		normalVector.gameObject.SetActive(value: false);
		antiNormalVector.gameObject.SetActive(value: false);
		radialInVector.gameObject.SetActive(value: false);
		radialOutVector.gameObject.SetActive(value: false);
		progradeWaypoint.gameObject.SetActive(value: false);
		retrogradeWaypoint.gameObject.SetActive(value: false);
		goto IL_06a5;
		IL_0154:
		relativeGymbal = attitudeGymbal * Quaternion.LookRotation(Vector3.ProjectOnPlane(currentMainBody.position + (Vector3d)currentMainBody.transform.up * currentMainBody.Radius - target.position, (target.position - currentMainBody.position).normalized).normalized, (target.position - currentMainBody.position).normalized);
		navBall.rotation = relativeGymbal;
		switch (FlightGlobals.speedDisplayMode)
		{
		case FlightGlobals.SpeedDisplayModes.Orbit:
			displayVelocity = FlightGlobals.ship_obtVelocity;
			break;
		case FlightGlobals.SpeedDisplayModes.Surface:
			displayVelocity = FlightGlobals.ship_srfVelocity;
			break;
		case FlightGlobals.SpeedDisplayModes.Target:
			displayVelocity = FlightGlobals.ship_tgtVelocity;
			break;
		}
		if (FlightGlobals.ActiveVessel.isEVA)
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
			if (FlightGlobals.ActiveVessel.LandedOrSplashed)
			{
				goto IL_0312;
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
			if (FlightGlobals.ActiveVessel.heightFromTerrain >= 0f)
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
				if (FlightGlobals.ActiveVessel.heightFromTerrain <= 1f)
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
					goto IL_0312;
				}
			}
		}
		DrawOrbitalCues(FlightGlobals.speedDisplayMode == FlightGlobals.SpeedDisplayModes.Orbit);
		displaySpeed = displayVelocity.magnitude;
		if (displaySpeed == 0f)
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
			displaySpeed = 1E-06f;
		}
		displayVelDir = displayVelocity / displaySpeed;
		GameObject obj = progradeVector.gameObject;
		int active;
		if (displaySpeed > vectorVelocityThreshold)
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
			active = ((progradeVector.transform.localPosition.z >= vectorUnitCutoff) ? 1 : 0);
		}
		else
		{
			active = 0;
		}
		obj.SetActive((byte)active != 0);
		progradeVector.localPosition = attitudeGymbal * (displayVelDir * vectorUnitScale);
		GameObject obj2 = retrogradeVector.gameObject;
		int active2;
		if (displaySpeed > vectorVelocityThreshold)
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
			active2 = ((retrogradeVector.transform.localPosition.z > vectorUnitCutoff) ? 1 : 0);
		}
		else
		{
			active2 = 0;
		}
		obj2.SetActive((byte)active2 != 0);
		retrogradeVector.localPosition = attitudeGymbal * (-displayVelDir * vectorUnitScale);
		GameObject obj3 = progradeWaypoint.gameObject;
		int active3;
		if (FlightGlobals.fetch.vesselTargetTransform != null)
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
			active3 = ((progradeWaypoint.transform.localPosition.z >= vectorUnitCutoff) ? 1 : 0);
		}
		else
		{
			active3 = 0;
		}
		obj3.SetActive((byte)active3 != 0);
		if (FlightGlobals.fetch.vesselTargetDirection != Vector3.zero)
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
			progradeWaypoint.localPosition = attitudeGymbal * FlightGlobals.fetch.vesselTargetDirection * vectorUnitScale;
		}
		GameObject obj4 = retrogradeWaypoint.gameObject;
		int active4;
		if (FlightGlobals.fetch.vesselTargetTransform != null)
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
			active4 = ((retrogradeWaypoint.transform.localPosition.z > vectorUnitCutoff) ? 1 : 0);
		}
		else
		{
			active4 = 0;
		}
		obj4.SetActive((byte)active4 != 0);
		if (FlightGlobals.fetch.vesselTargetDirection != Vector3.zero)
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
			retrogradeWaypoint.localPosition = attitudeGymbal * -FlightGlobals.fetch.vesselTargetDirection * vectorUnitScale;
		}
		SetVectorAlphaTint(progradeVector);
		SetVectorAlphaTint(retrogradeVector);
		SetVectorAlphaTint(progradeWaypoint);
		SetVectorAlphaTint(retrogradeWaypoint);
		goto IL_06a5;
		IL_06a5:
		if (FlightGlobals.ActiveVessel.situation == Vessel.Situations.PRELAUNCH)
		{
			while (true)
			{
				switch (4)
				{
				case 0:
					break;
				default:
					if (FlightGlobals.ActiveVessel.ctrlState.pitch == 0f)
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
						if (FlightGlobals.ActiveVessel.ctrlState.yaw == 0f)
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
							if (FlightGlobals.ActiveVessel.ctrlState.roll == 0f)
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
								if (initialHeadingSet)
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
									break;
								}
							}
						}
					}
					headingText.text = KSPUtil.LocalizeNumber(Quaternion.Inverse(relativeGymbal).eulerAngles.y, "000") + "°";
					initialHeadingSet = true;
					return;
				}
			}
		}
		headingText.text = KSPUtil.LocalizeNumber(Quaternion.Inverse(relativeGymbal).eulerAngles.y, "000") + "°";
		initialHeadingSet = true;
	}

	private void SetVectorAlphaTint(Transform vector)
	{
		float num = Mathf.Clamp01(Vector3.Dot(vector.localPosition.normalized, Vector3.forward));
		float num2 = Vector3.Dot(vector.localPosition.normalized, Vector3.up);
		float num3 = num;
		if (num2 >= 0.65f)
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
			num3 *= Mathf.Clamp01(Mathf.InverseLerp(0.9f, 0.65f, num2));
		}
		else if (num2 <= -0.75f)
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
			num3 *= Mathf.Clamp01(Mathf.InverseLerp(-0.95f, -0.75f, num2));
		}
		vector.GetComponent<MeshRenderer>().materials[0].SetFloat("_Opacity", num3);
	}

	private void DrawOrbitalCues(bool drawCondition)
	{
		Vessel activeVessel = FlightGlobals.ActiveVessel;
		int num;
		if (activeVessel.orbit != null)
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
			if (1 == 0)
			{
				/*OpCode not supported: LdMemberToken*/;
			}
			num = ((activeVessel.orbit.referenceBody != null) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		if (((uint)num & (drawCondition ? 1u : 0u)) != 0)
		{
			while (true)
			{
				switch (7)
				{
				case 0:
					break;
				default:
					wCoM = activeVessel.CurrentCoM;
					obtVel = activeVessel.orbit.GetVel();
					cbPos = activeVessel.mainBody.position;
					radial = Vector3.ProjectOnPlane((wCoM - cbPos).normalized, obtVel).normalized;
					normal = Vector3.Cross(radial, obtVel.normalized);
					radial = attitudeGymbal * radial * vectorUnitScale;
					normal = attitudeGymbal * normal * vectorUnitScale;
					antiNormalVector.gameObject.SetActive(normal.z > vectorUnitCutoff);
					normalVector.gameObject.SetActive(normal.z < 0f - vectorUnitCutoff);
					antiNormalVector.localPosition = normal;
					normalVector.localPosition = -normal;
					SetVectorAlphaTint(antiNormalVector);
					SetVectorAlphaTint(normalVector);
					radialInVector.gameObject.SetActive(radial.z < 0f - vectorUnitCutoff);
					radialOutVector.gameObject.SetActive(radial.z > vectorUnitCutoff);
					radialInVector.localPosition = -radial;
					radialOutVector.localPosition = radial;
					SetVectorAlphaTint(radialInVector);
					SetVectorAlphaTint(radialOutVector);
					return;
				}
			}
		}
		if (radialInVector.gameObject.activeSelf)
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
			radialInVector.gameObject.SetActive(value: false);
		}
		if (radialOutVector.gameObject.activeSelf)
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
			radialOutVector.gameObject.SetActive(value: false);
		}
		if (normalVector.gameObject.activeSelf)
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
			normalVector.gameObject.SetActive(value: false);
		}
		if (!antiNormalVector.gameObject.activeSelf)
		{
			return;
		}
		while (true)
		{
			switch (6)
			{
			case 0:
				continue;
			}
			antiNormalVector.gameObject.SetActive(value: false);
			return;
		}
	}

	public void SetWaypoint(Transform target)
	{
	}

	public void ClearWaypoint()
	{
	}
}
You are not using the latest version of the tool, please update.
Latest version is '11.1.0.9782' (yours is '8.2.0.7535-95108c96')
