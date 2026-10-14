using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Geuneda.Analytics.Airbridge;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Airbridge.Tests
{
	public sealed class AirbridgeAnalyticsSinkTest
	{
		private static readonly AnalyticsAppInfo App = new AnalyticsAppInfo("i", "1.0", "android", "dev", "d", "o");

		private FakeApi _api;

		[SetUp]
		public void SetUp()
		{
			_api = new FakeApi();
		}

		private AirbridgeAnalyticsSink Sink(AirbridgeAnalyticsSinkOptions options = null)
		{
			return new AirbridgeAnalyticsSink(options, _api);
		}

		private static AnalyticsEvent Event(string name, params (string Name, object Value)[] parameters)
		{
			return UserEvent(name, null, null, parameters);
		}

		private static AnalyticsEvent UserEvent(string name, string userId, IReadOnlyDictionary<string, string> properties,
			params (string Name, object Value)[] parameters)
		{
			return new AnalyticsEvent(Guid.NewGuid().ToString("N"), name,
				parameters.Select(p => new AnalyticsParameter(p.Name, AnalyticsValue.FromObject(p.Value))).ToArray(), DateTime.UtcNow, false, 1, null, userId,
				properties, App);
		}

		private static void Send(AirbridgeAnalyticsSink sink, params AnalyticsEvent[] events)
		{
			Assert.AreEqual(AnalyticsSendResult.Delivered, sink.SendAsync(events, CancellationToken.None).GetAwaiter().GetResult());
		}

		[Test]
		public void GenericEvent_SplitsSemanticAndCustomAttributes()
		{
			var options = new AirbridgeAnalyticsSinkOptions();
			options.Categories["tutorial_done"] = AirbridgeCategory.COMPLETE_TUTORIAL;
			var sink = Sink(options);
			options.Categories["late"] = "ignored";

			Send(sink, Event("tutorial_done", ("action", "Tutorial"), ("label", "Step3"), ("step", 3)), Event("late"), Event("plain"));

			Assert.AreEqual(AirbridgeCategory.COMPLETE_TUTORIAL, _api.Events[0].Category);
			Assert.AreEqual("Tutorial", _api.Events[0].Semantic[AirbridgeAttribute.ACTION]);
			Assert.AreEqual("Step3", _api.Events[0].Semantic[AirbridgeAttribute.LABEL]);
			Assert.AreEqual(3L, _api.Events[0].Custom["step"]);
			Assert.AreEqual("late", _api.Events[1].Category, "options are copied when the sink is made");
			Assert.IsNull(_api.Events[2].Semantic);
			Assert.IsNull(_api.Events[2].Custom);
		}

		[Test]
		public void Purchase_BecomesOrderCompleted()
		{
			Send(Sink(), Event(AnalyticsStandardEvents.Purchase, ("product_id", "gem_100"), ("price", 1.5), ("quantity", 2), ("value", 3.0),
				("currency", "USD"), ("transaction_id", "tx"), ("product_name", "Gems"), ("player_level", 9)));

			var (category, semantic, custom, _, _) = _api.Events.Single();
			Assert.AreEqual(AirbridgeCategory.ORDER_COMPLETED, category);
			Assert.AreEqual(3.0, semantic[AirbridgeAttribute.VALUE]);
			Assert.AreEqual("USD", semantic[AirbridgeAttribute.CURRENCY]);
			Assert.AreEqual("tx", semantic[AirbridgeAttribute.TRANSACTION_ID]);
			Assert.AreEqual(true, semantic[AirbridgeAttribute.IN_APP_PURCHASED]);
			Assert.AreEqual(2, semantic[AirbridgeAttribute.TOTAL_QUANTITY]);
			var product = ((List<Dictionary<string, object>>) semantic[AirbridgeAttribute.PRODUCTS]).Single();
			Assert.AreEqual("gem_100", product[AirbridgeAttribute.PRODUCT_ID]);
			Assert.AreEqual("Gems", product[AirbridgeAttribute.PRODUCT_NAME]);
			Assert.AreEqual(1.5, product[AirbridgeAttribute.PRODUCT_PRICE]);
			CollectionAssert.AreEquivalent(new[] { "player_level" }, custom.Keys);
		}

		[Test]
		public void AdImpression_FromAppLovin_AddsAdPartners()
		{
			Send(Sink(), Event(AnalyticsStandardEvents.AdImpression, ("ad_platform", "AppLovin"), ("ad_source", "AdMob"), ("ad_format", "rewarded"),
					("ad_unit_name", "u1"), ("ad_placement", "shop"), ("value", 0.002), ("currency", "USD"), ("country_code", "KR")),
				Event(AnalyticsStandardEvents.AdImpression, ("ad_platform", "LevelPlay"), ("value", 0.001), ("currency", "USD")));

			var (category, semantic, custom, _, _) = _api.Events[0];
			Assert.AreEqual(AirbridgeCategory.AD_IMPRESSION, category);
			Assert.AreEqual(0.002, semantic[AirbridgeAttribute.VALUE]);
			var appLovin = (Dictionary<string, object>) ((Dictionary<string, object>) semantic[AirbridgeAttribute.AD_PARTNERS])["appLovin"];
			Assert.AreEqual(0.002, appLovin["revenue"]);
			Assert.AreEqual("AdMob", appLovin["network_name"]);
			Assert.AreEqual("u1", appLovin["adunit_identifier"]);
			Assert.AreEqual("shop", appLovin["placement"]);
			Assert.AreEqual("KR", appLovin["country_code"]);
			Assert.AreEqual("rewarded", custom["ad_format"]);
			Assert.AreEqual("AppLovin", custom["ad_platform"]);

			Assert.IsFalse(_api.Events[1].Semantic.ContainsKey(AirbridgeAttribute.AD_PARTNERS), "only AppLovin has a known partner mapping");
		}

		[Test]
		public void Events_AreAttributedToTheUserAtLogTime()
		{
			var sink = Sink();
			sink.SetUserId("B");
			sink.SetUserProperty("tier", "silver");

			Send(sink, UserEvent("first", "A", new Dictionary<string, string> { { "tier", "gold" }, { "region", "kr" } }),
				UserEvent("second", "B", new Dictionary<string, string> { { "tier", "silver" } }));

			Assert.AreEqual(("A", "gold", "kr"), (_api.Events[0].UserId, _api.Events[0].Attributes["tier"], _api.Events[0].Attributes["region"]));
			Assert.AreEqual("B", _api.Events[1].UserId);
			Assert.IsFalse(_api.Events[1].Attributes.ContainsKey("region"));
			Assert.AreEqual("B", _api.CurrentUserId);
			Assert.AreEqual("silver", _api.CurrentAttributes["tier"]);
		}

		[Test]
		public void FailedBatch_StillRestoresTheCurrentUser()
		{
			var sink = Sink(new AirbridgeAnalyticsSinkOptions { HandleEvent = _ => throw new InvalidOperationException("game bug") });
			sink.SetUserId("B");
			sink.SetUserProperty("tier", "silver");

			Assert.Throws<InvalidOperationException>(() =>
				sink.SendAsync(new[] { UserEvent("first", "A", new Dictionary<string, string> { { "tier", "gold" } }) }, CancellationToken.None)
					.GetAwaiter().GetResult());

			Assert.AreEqual("B", _api.CurrentUserId);
			Assert.AreEqual("silver", _api.CurrentAttributes["tier"]);
		}

		[Test]
		public void Removal_ReachesTheSdkEvenForAttributesTheSinkNeverSaw()
		{
			Sink().SetUserProperty("tier", "gold");

			Sink().SetUserProperty("tier", null);

			Assert.IsFalse(_api.CurrentAttributes.ContainsKey("tier"));
		}

		[Test]
		public void HandleEvent_AndUserInfo()
		{
			var sink = Sink(new AirbridgeAnalyticsSinkOptions { HandleEvent = e => e.Name == "special" });

			Send(sink, Event("special"), Event("normal"));
			sink.SetUserId("u1");
			sink.SetUserId(null);
			sink.SetUserProperty("tier", "gold");
			sink.SetUserProperty("tier", null);

			Assert.IsTrue(sink.IsReady);
			CollectionAssert.AreEqual(new[] { "normal" }, _api.Events.Select(e => e.Category));
			CollectionAssert.AreEqual(new[] { null, "u1", null }, _api.UserIds, "the first call sets the known state of the SDK");
			CollectionAssert.AreEqual(new[] { ("tier", "gold"), ("tier", (string) null) }, _api.Attributes);
		}

		[Test]
		public void CancelledToken_ReturnsRetryWithoutSending()
		{
			var result = Sink().SendAsync(new[] { Event("e") }, new CancellationToken(true)).GetAwaiter().GetResult();

			Assert.AreEqual(AnalyticsSendResult.Retry, result);
			Assert.AreEqual(0, _api.Events.Count);
		}

		private sealed class FakeApi : IAirbridgeApi
		{
			public List<(string Category, Dictionary<string, object> Semantic, Dictionary<string, object> Custom, string UserId, Dictionary<string, string> Attributes)> Events { get; } =
				new List<(string, Dictionary<string, object>, Dictionary<string, object>, string, Dictionary<string, string>)>();

			public List<string> UserIds { get; } = new List<string>();
			public List<(string Key, string Value)> Attributes { get; } = new List<(string, string)>();
			public string CurrentUserId { get; private set; }
			public Dictionary<string, string> CurrentAttributes { get; } = new Dictionary<string, string>();

			public void TrackEvent(string category, Dictionary<string, object> semanticAttributes, Dictionary<string, object> customAttributes)
			{
				Events.Add((category, semanticAttributes, customAttributes, CurrentUserId, new Dictionary<string, string>(CurrentAttributes)));
			}

			public void SetUserId(string userId)
			{
				UserIds.Add(userId);
				CurrentUserId = userId;
			}

			public void SetUserAttribute(string key, string value)
			{
				Attributes.Add((key, value));

				if (value == null)
				{
					CurrentAttributes.Remove(key);
				}
				else
				{
					CurrentAttributes[key] = value;
				}
			}
		}
	}
}
