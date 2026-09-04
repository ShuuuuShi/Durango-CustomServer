using UnityEngine;

namespace Durango.UI;

public class FatigueGaugeScrollSprite : MonoBehaviour
{
	public enum ScrollDirection
	{
		Vertical,
		Horizontal
	}

	[SerializeField]
	private UISprite _scrollSprite;

	[SerializeField]
	private float _speed;

	[SerializeField]
	private ScrollDirection _scrollDirection;

	private UIPanel _parent;

	private Transform _spriteTransform;

	private float _scrollLength;

	private Vector3 _defaultAngle;

	public float Speed
	{
		get
		{
			return _speed;
		}
		set
		{
			_speed = value;
		}
	}

	private void Start()
	{
		_spriteTransform = _scrollSprite.transform;
		_spriteTransform = _scrollSprite.transform;
		UISpriteData atlasSprite = _scrollSprite.GetAtlasSprite();
		_scrollLength = ((_scrollDirection != ScrollDirection.Vertical) ? ((float)atlasSprite.width * _spriteTransform.localScale.x) : ((float)atlasSprite.height * _spriteTransform.localScale.y));
		_defaultAngle = _spriteTransform.localEulerAngles;
		_parent = GetComponent<UIPanel>();
	}

	private void Update()
	{
		_spriteTransform.localEulerAngles = ((!(Speed > 0f)) ? (_defaultAngle + Vector3.forward * 180f) : _defaultAngle);
		Vector3 localPosition = _spriteTransform.localPosition;
		float num = ((_scrollDirection != ScrollDirection.Vertical) ? localPosition.x : localPosition.y);
		num += Speed * Time.deltaTime;
		num = Mathf.Repeat(num, _scrollLength);
		if (_scrollDirection == ScrollDirection.Vertical)
		{
			localPosition.y = num;
		}
		else
		{
			localPosition.x = num;
		}
		_spriteTransform.localPosition = localPosition;
		_parent.alpha = Mathf.Abs(Speed);
	}
}
