using System;
using System.Collections.Generic;
using Durango.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace Durango.Render;

public class PlaneShadowManager : Singleton<PlaneShadowManager>
{
	[SerializeField]
	private Transform _plane;

	[SerializeField]
	private Transform _lightDir;

	[SerializeField]
	private Material _material;

	private readonly Dictionary<Mesh, HashSet<Quaternion>> _expandedMesh = new Dictionary<Mesh, HashSet<Quaternion>>();

	[ExposedInEditor("그림자 각도 매 프레임 업데이트")]
	private bool _debugShadowUpdate;

	public Matrix4x4 ProjectionMatrix { get; private set; }

	public Material Material => _material;

	public static ShadowOption Option { get; private set; }

	public static event Action OptionChanged;

	private void Start()
	{
		UpdateShadowMatrix();
	}

	public void UpdateShadowMatrix()
	{
		Vector3 forward = _lightDir.forward;
		Vector3 rhs = -_plane.up;
		float num = Vector3.Dot(forward, rhs);
		ProjectionMatrix = new Matrix4x4
		{
			[0, 0] = num - rhs.x * forward.x,
			[1, 0] = (0f - rhs.x) * forward.y,
			[2, 0] = (0f - rhs.x) * forward.z,
			[3, 0] = 0f,
			[0, 1] = (0f - rhs.y) * forward.x,
			[1, 1] = num - rhs.y * forward.y,
			[2, 1] = (0f - rhs.y) * forward.z,
			[3, 1] = 0f,
			[0, 2] = (0f - rhs.z) * forward.x,
			[1, 2] = (0f - rhs.z) * forward.y,
			[2, 2] = num - rhs.z * forward.z,
			[3, 2] = 0f,
			[0, 3] = 0f,
			[1, 3] = 0f,
			[2, 3] = 0f,
			[3, 3] = num
		};
		Shader.SetGlobalMatrix("_projectionMatrix", ProjectionMatrix);
		Shader.SetGlobalVector("_planeNormal", _plane.up);
	}

	public static void ExpandBound(GameObject target)
	{
		MeshRenderer[] componentsInChildren = target.GetComponentsInChildren<MeshRenderer>();
		int i = 0;
		for (int size = KUtility.GetSize(componentsInChildren); i < size; i++)
		{
			MeshRenderer meshRenderer = componentsInChildren[i];
			if (!(meshRenderer == null) && meshRenderer.shadowCastingMode == ShadowCastingMode.On)
			{
				MeshFilter component = meshRenderer.GetComponent<MeshFilter>();
				if (!(component == null) && !(component.sharedMesh == null))
				{
					Singleton<PlaneShadowManager>.Instance().ApplyExpandedBounds(component.sharedMesh, component.transform.rotation);
				}
			}
		}
	}

	private void ApplyExpandedBounds(Mesh mesh, Quaternion rotation)
	{
		if (_expandedMesh.ContainsKey(mesh))
		{
			if (_expandedMesh[mesh].Contains(rotation))
			{
				return;
			}
		}
		else
		{
			_expandedMesh.Add(mesh, new HashSet<Quaternion>());
		}
		_expandedMesh[mesh].Add(rotation);
		Vector3 expandTarget = ResourceSingleton<ShadowBounds>.Instance().GetExpandTarget(mesh, rotation);
		Bounds bounds = mesh.bounds;
		bounds.Encapsulate(expandTarget);
		mesh.bounds = bounds;
	}

	public static void ChangeOption(ShadowOption shadowOption)
	{
		if (GameManager.IsPrologueMode)
		{
			shadowOption = ShadowOption.Normal;
		}
		if (Option != shadowOption)
		{
			Option = shadowOption;
			if (OptionChanged != null)
			{
				OptionChanged();
			}
		}
	}
}
