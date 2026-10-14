using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Airbridge
{
	/// <summary>
	/// Airbridge 로 이벤트를 보내는 sink 입니다. 이벤트 이름은 카테고리(<see cref="AirbridgeAnalyticsSinkOptions.Categories"/> 로 바꿀 수 있음), 파라미터는 시맨틱·커스텀 속성으로 나눠 보냅니다.
	/// 표준 이벤트는 Airbridge 표준 카테고리로 바꿉니다: 구매는 주문 완료(<c>AirbridgeCategory.ORDER_COMPLETED</c>, 상품 목록·거래 ID·인앱 구매 표시),
	/// 광고 노출 수익은 광고 노출(<c>AirbridgeCategory.AD_IMPRESSION</c>, 플랫폼이 AppLovin 이면 <c>adPartners.appLovin</c>, 광고 필드는 커스텀 속성).
	/// </summary>
	/// <remarks>
	/// Airbridge SDK 는 앱 시작 때 네이티브에서 스스로 초기화하므로 항상 준비된 것으로 봅니다(에디터에서는 SDK 가 아무것도 하지 않음).
	/// 이벤트마다 기록할 때의 사용자 ID·속성을 SDK 에 맞춘 뒤 보내고, 배치가 끝나면 지금 상태로 되돌립니다(저장했던 이벤트가 그 뒤에 로그인한 사용자에게 붙지 않게).
	/// </remarks>
	public sealed class AirbridgeAnalyticsSink : IAnalyticsSink
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.Analytics.Airbridge");

		// 광고 파트너 상세의 AppLovin MAX 필드 가운데 표준 파라미터에 없는 것. 추가 파라미터에 같은 이름으로 넣으면 함께 보냅니다
		private static readonly string[] AppLovinExtraFields = { "country_code", "network_placement", "creative_identifier", "dsp_name" };

		private readonly IAirbridgeApi _api;
		private readonly Func<AnalyticsEvent, bool> _handleEvent;
		private readonly Dictionary<string, string> _categories;
		private readonly Dictionary<string, string> _semanticAttributes;

		// 분석 서비스가 알려 준 지금 사용자 상태와 SDK 에 마지막으로 넘긴 상태
		private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<string, string> _sdkProperties = new Dictionary<string, string>(StringComparer.Ordinal);
		private string _userId;
		private bool _sdkUserIdKnown;
		private string _sdkUserId;
		private IReadOnlyDictionary<string, string> _sdkPropertiesSource;

		public AirbridgeAnalyticsSink(AirbridgeAnalyticsSinkOptions options = null) : this(options, new AirbridgeApi())
		{
		}

		internal AirbridgeAnalyticsSink(AirbridgeAnalyticsSinkOptions options, IAirbridgeApi api)
		{
			options = options ?? new AirbridgeAnalyticsSinkOptions();
			_api = api;
			_handleEvent = options.HandleEvent;
			_categories = new Dictionary<string, string>(options.Categories, StringComparer.Ordinal);
			_semanticAttributes = new Dictionary<string, string>(options.SemanticAttributes, StringComparer.Ordinal);
		}

		public AnalyticsSinks Id => AnalyticsSinks.Airbridge;

		public int MaxBatchSize => 50;

		public TimeSpan BatchDelay => TimeSpan.Zero;

		public bool IsReady => true;

		public UniTask<AnalyticsSendResult> SendAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken cancellationToken)
		{
			// SDK 호출은 동기라 배치 도중에는 취소되지 않습니다. 서비스를 이미 닫았으면 보내지 않고 저장해 두도록 재시도로 돌려줍니다
			if (cancellationToken.IsCancellationRequested)
			{
				return UniTask.FromResult(AnalyticsSendResult.Retry);
			}

			var completed = false;

			try
			{
				foreach (var analyticsEvent in events)
				{
					// 게임이 직접 보내는 이벤트도 기록할 때의 사용자에게 붙습니다
					ApplyUser(analyticsEvent.UserId, analyticsEvent.UserProperties);

					if (_handleEvent != null && _handleEvent(analyticsEvent))
					{
						continue;
					}

					Send(analyticsEvent);
				}

				completed = true;
			}
			finally
			{
				RestoreCurrentUser(completed);
			}

			return UniTask.FromResult(AnalyticsSendResult.Delivered);
		}

		public void SetUserId(string userId)
		{
			_userId = userId;
			ApplyUserId(userId);
		}

		public void SetUserProperty(string name, string value)
		{
			if (value != null)
			{
				_properties[name] = value;
				ApplyUser(_userId, _properties);
				return;
			}

			// 지우라는 요청은 이 sink 가 그 속성을 모르더라도(이전 실행에서 남은 값) SDK 에 넘깁니다
			_properties.Remove(name);
			_sdkPropertiesSource = null;
			_api.SetUserAttribute(name, null);
			_sdkProperties.Remove(name);
		}

		/// <summary>배치가 끝나면(예외로 끝나도) SDK 를 지금 사용자 상태로 되돌립니다. 예외로 끝난 경우 되돌리기 실패는 원래 예외를 가리지 않게 로그로만 남깁니다.</summary>
		private void RestoreCurrentUser(bool completed)
		{
			if (completed)
			{
				ApplyUser(_userId, _properties);
				return;
			}

			try
			{
				ApplyUser(_userId, _properties);
			}
			catch (Exception exception)
			{
				Logger.Error(exception, "Could not restore the current Airbridge user after a failed batch");
			}
		}

		/// <summary>SDK 의 사용자 ID·속성을 맞춥니다. 바뀐 것만 넘기고, 없어진 속성은 지웁니다.</summary>
		private void ApplyUser(string userId, IReadOnlyDictionary<string, string> properties)
		{
			ApplyUserId(userId);

			// 분석 서비스는 속성이 바뀔 때만 새 사전을 만들므로 같은 사전이면 이미 맞춰져 있습니다
			if (ReferenceEquals(properties, _sdkPropertiesSource) && !ReferenceEquals(properties, _properties))
			{
				return;
			}

			// 중간에 실패하면 SDK 상태가 어느 사전과도 같지 않으므로 다시 비교하도록 지웁니다
			_sdkPropertiesSource = null;

			foreach (var name in new List<string>(_sdkProperties.Keys))
			{
				if (!properties.ContainsKey(name))
				{
					_api.SetUserAttribute(name, null);
					_sdkProperties.Remove(name);
				}
			}

			foreach (var property in properties)
			{
				if (!_sdkProperties.TryGetValue(property.Key, out var current) || current != property.Value)
				{
					_api.SetUserAttribute(property.Key, property.Value);
					_sdkProperties[property.Key] = property.Value;
				}
			}

			_sdkPropertiesSource = properties;
		}

		private void ApplyUserId(string userId)
		{
			if (_sdkUserIdKnown && _sdkUserId == userId)
			{
				return;
			}

			_api.SetUserId(userId);
			_sdkUserId = userId;
			_sdkUserIdKnown = true;
		}

		private void Send(AnalyticsEvent analyticsEvent)
		{
			if (AnalyticsPurchase.TryRead(analyticsEvent, out var purchase))
			{
				_api.TrackEvent(AirbridgeCategory.ORDER_COMPLETED, Purchase(purchase), Extras(analyticsEvent, AnalyticsPurchase.IsStandardParameter));
				return;
			}

			if (AnalyticsAdImpression.TryRead(analyticsEvent, out var impression))
			{
				var custom = Extras(analyticsEvent, AnalyticsAdImpression.IsStandardParameter) ?? new Dictionary<string, object>();
				custom[AnalyticsStandardParameters.AdPlatform] = impression.Platform;
				AddOptional(custom, AnalyticsStandardParameters.AdSource, impression.Source);
				AddOptional(custom, AnalyticsStandardParameters.AdFormat, impression.Format);
				AddOptional(custom, AnalyticsStandardParameters.AdUnitName, impression.UnitName);
				AddOptional(custom, AnalyticsStandardParameters.AdPlacement, impression.Placement);
				_api.TrackEvent(AirbridgeCategory.AD_IMPRESSION, AdImpression(impression, analyticsEvent), custom);
				return;
			}

			Dictionary<string, object> semantic = null;
			Dictionary<string, object> customAttributes = null;

			foreach (var parameter in analyticsEvent.Parameters)
			{
				if (_semanticAttributes.TryGetValue(parameter.Name, out var key))
				{
					(semantic = semantic ?? new Dictionary<string, object>())[key] = parameter.Value.ToObject();
				}
				else
				{
					(customAttributes = customAttributes ?? new Dictionary<string, object>())[parameter.Name] = parameter.Value.ToObject();
				}
			}

			var category = _categories.TryGetValue(analyticsEvent.Name, out var mapped) ? mapped : analyticsEvent.Name;
			_api.TrackEvent(category, semantic, customAttributes);
		}

		private static Dictionary<string, object> Purchase(AnalyticsPurchase purchase)
		{
			var product = new Dictionary<string, object>
			{
				{ AirbridgeAttribute.PRODUCT_ID, purchase.ProductId },
				{ AirbridgeAttribute.PRODUCT_PRICE, purchase.Price },
				{ AirbridgeAttribute.PRODUCT_QUANTITY, purchase.Quantity },
				{ AirbridgeAttribute.PRODUCT_CURRENCY, purchase.Currency }
			};

			if (purchase.ProductName != null)
			{
				product.Add(AirbridgeAttribute.PRODUCT_NAME, purchase.ProductName);
			}

			var semantic = new Dictionary<string, object>
			{
				{ AirbridgeAttribute.VALUE, purchase.Value },
				{ AirbridgeAttribute.CURRENCY, purchase.Currency },
				{ AirbridgeAttribute.TOTAL_QUANTITY, purchase.Quantity },
				{ AirbridgeAttribute.IN_APP_PURCHASED, true },
				{ AirbridgeAttribute.PRODUCTS, new List<Dictionary<string, object>> { product } }
			};

			if (purchase.TransactionId != null)
			{
				semantic.Add(AirbridgeAttribute.TRANSACTION_ID, purchase.TransactionId);
			}

			return semantic;
		}

		private static Dictionary<string, object> AdImpression(AnalyticsAdImpression impression, AnalyticsEvent analyticsEvent)
		{
			var semantic = new Dictionary<string, object>
			{
				{ AirbridgeAttribute.VALUE, impression.Revenue },
				{ AirbridgeAttribute.CURRENCY, impression.Currency }
			};

			if (!IsAppLovin(impression.Platform))
			{
				return semantic;
			}

			var appLovin = new Dictionary<string, object> { { "revenue", impression.Revenue } };
			AddOptional(appLovin, "network_name", impression.Source);
			AddOptional(appLovin, "adunit_identifier", impression.UnitName);
			AddOptional(appLovin, "placement", impression.Placement);

			foreach (var field in AppLovinExtraFields)
			{
				if (analyticsEvent.TryGetParameter(field, out var value))
				{
					appLovin[field] = value.ToObject();
				}
			}

			semantic.Add(AirbridgeAttribute.AD_PARTNERS, new Dictionary<string, object> { { "appLovin", appLovin } });
			return semantic;
		}

		private static bool IsAppLovin(string platform)
		{
			var normalized = platform.Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
			return normalized == "applovin" || normalized == "applovinmax" || normalized == "max";
		}

		private static Dictionary<string, object> Extras(AnalyticsEvent analyticsEvent, Func<string, bool> isStandard)
		{
			Dictionary<string, object> extras = null;

			foreach (var parameter in analyticsEvent.Parameters)
			{
				if (!isStandard(parameter.Name))
				{
					(extras = extras ?? new Dictionary<string, object>())[parameter.Name] = parameter.Value.ToObject();
				}
			}

			return extras;
		}

		private static void AddOptional(Dictionary<string, object> target, string key, string value)
		{
			if (value != null)
			{
				target[key] = value;
			}
		}
	}
}
