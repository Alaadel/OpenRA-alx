﻿#region Copyright & License Information
/*
 * Copyright 2007,2009,2010 Chris Forbes, Robert Pepperell, Matthew Bowra-Dean, Paul Chote, Alli Witheford.
 * This file is part of OpenRA.
 * 
 *  OpenRA is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 * 
 *  OpenRA is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 * 
 *  You should have received a copy of the GNU General Public License
 *  along with OpenRA.  If not, see <http://www.gnu.org/licenses/>.
 */
#endregion

using System;
using System.Linq;
using OpenRA.Traits.Activities;

namespace OpenRA.Traits
{
	class RepairableInfo : ITraitInfo
	{
		public readonly string[] RepairBuildings = { "fix" };
		public object Create(Actor self) { return new Repairable(self); }
	}

	class Repairable : IIssueOrder, IResolveOrder
	{
		public Repairable(Actor self) { }

		public Order IssueOrder(Actor self, int2 xy, MouseInput mi, Actor underCursor)
		{
			if (mi.Button != MouseButton.Right) return null;
			if (underCursor == null) return null;

			if (self.Info.Traits.Get<RepairableInfo>().RepairBuildings.Contains(underCursor.Info.Name)
				&& underCursor.Owner == self.Owner)
				return new Order("Enter", self, underCursor);

			return null;
		}

		public void ResolveOrder(Actor self, Order order)
		{

			if (order.OrderString == "Enter")
			{

				var res = order.TargetActor.traits.GetOrDefault<Reservable>();
                var wp = order.TargetActor.traits.GetOrDefault<RallyPoint>().rallyPoint;

				self.CancelActivity();
				self.QueueActivity(new Move(((1 / 24f) * order.TargetActor.CenterLocation).ToInt2(), order.TargetActor));
				self.QueueActivity(new Rearm());
				self.QueueActivity(new Repair());
                if (order.TargetActor.traits.Contains<RallyPoint>())
                    self.QueueActivity(new CallFunc(
                    () => self.QueueActivity(new Move(order.TargetActor.traits.Get<RallyPoint>().rallyPoint,
                    order.TargetActor))));
			}
		}
	}
}
