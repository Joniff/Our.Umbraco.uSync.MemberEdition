using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Jumoo.uSync.BackOffice;
using Jumoo.uSync.BackOffice.Helpers;
using Jumoo.uSync.Core;
using Jumoo.uSync.Core.Mappers;
using Umbraco.Core;
using Umbraco.Core.Cache;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence;
using Umbraco.Core.Services;
using uSync.MemberEdition.Rdbms;
using uSync.MemberEdition.Security;
using uSync.MemberEdition.Xml;

namespace uSync.MemberEdition.Serializers
{
	public class MemberHandler : ISyncHandler
	{
		public string Name { get { return "uSync: MemberHandler"; } }
		public int Priority { get { return Jumoo.uSync.BackOffice.uSyncConstants.Priority.Content + 1; } }
		public string SyncFolder { get { return "Member"; } }

		private IMemberService _memberService;
		private IMemberTypeService _memberTypeService;
		private IMemberGroupService _memberGroupService;
		private IRuntimeCacheProvider _runtimeCacheProvider;
		private UmbracoDatabase Database;

		private const string NodeName = "Member";
		private const string KeyAttribute = "Email";
		private const string TypeAttribute = "Type";
		private const string AliasAttribute = "Alias";
		private const string NameAttribute = "Name";
		private const string UserAttribute = "User";

		private const string CommentsNode = "Comments";
		private const string FailedPasswordAttemptsNode = "FailedPasswordAttempts";
		private const string GroupsNode = "Groups";
		private const string GroupNode = "Group";
		private const string IsApprovedNode = "IsApproved";
		private const string IsLockedOutNode = "IsLockedOut";
		private const string LastLockedOutDateNode = "LastLockedOutDate";
		private const string LastLoginDateNode = "LastLoginDate";
		private const string LastPasswordChangeDateNode = "LastPasswordChangeDate";
		private const string PasswordQuestionNode = "PasswordQuestion";
		private const string PasswordNode = "Password";
		private const string RawPasswordAnswerNode = "RawPasswordAnswer";

		private const string MemberTypeCacheKey = "544dd2b0-11d1-41b4-a346-3435dd2d80cf:";
		private const string FileExtension = "config";
		private const string FileFilter = "*." + FileExtension;

		public MemberHandler()
		{
			_memberService = ApplicationContext.Current.Services.MemberService;
			_memberTypeService = ApplicationContext.Current.Services.MemberTypeService;
			_memberGroupService = ApplicationContext.Current.Services.MemberGroupService;
			_runtimeCacheProvider = ApplicationContext.Current.ApplicationCache.RuntimeCache;
			Database = ApplicationContext.Current.DatabaseContext.Database;
		}

		private IMemberType MemberType(string key) => _runtimeCacheProvider.GetCacheItem<IMemberType>(MemberTypeCacheKey + key.ToLowerInvariant(), () =>
		{
			var member = _memberTypeService.GetAll().FirstOrDefault(x => string.Compare(x.Alias, key, true) == 0);
			if (member != null)
			{
				return member;
			}

			LogHelper.Warn<MemberHandler>($"Unknown MemberType called {key}");
			return null;
		});

		private string Filename(IMemberType memberType, IMember member) =>
			Umbraco.Core.IO.IOHelper.MapPath(uSyncBackOfficeContext.Instance.Configuration.Settings.Folder + "//" + SyncFolder + "//" + memberType.Alias + "//" + member.Email.ToSafeFileName().Replace('.', '-') + "." + FileExtension);


		public void RegisterEvents()
		{
			MemberService.Saved += MemberService_Saved;
			MemberService.Deleted += MemberService_Deleted;
		}

		private void MemberService_Saved(IMemberService sender, Umbraco.Core.Events.SaveEventArgs<IMember> e)
		{
			if (uSyncEvents.Paused)
			{
				return;
			}
			foreach (var member in e.SavedEntities)
			{
				var memberType = MemberType(member.ContentTypeAlias);
				ExportMember(memberType, member);
			}
		}

		private void MemberService_Deleted(IMemberService sender, Umbraco.Core.Events.DeleteEventArgs<IMember> e)
		{
			if (uSyncEvents.Paused)
			{
				return;
			}
			foreach (var member in e.DeletedEntities)
			{
				var memberType = MemberType(member.ContentTypeAlias);
				uSyncIOHelper.ArchiveFile(Filename(memberType, member));
			}
		}

		private XElement CreateProperty(Property prop)
		{
			try
			{
				return prop.ToXml();
			}
			catch
			{
				return new XElement(prop.Alias, prop.Value);
			}
		}


		private XElement Serialize(IMemberType memberType, IMember member)
		{
			var node = new XElement(NodeName,
				new XAttribute(KeyAttribute, member.Email),
				new XAttribute(TypeAttribute, memberType.Alias),
				new XAttribute(NameAttribute, member.Name),
				new XAttribute(UserAttribute, member.Username)
			);

			node.Add(new XElement(CommentsNode, member.Comments));
			node.Add(new XElement(FailedPasswordAttemptsNode, member.FailedPasswordAttempts));
			node.Add(new XElement(IsApprovedNode, member.IsApproved));
			node.Add(new XElement(IsLockedOutNode, member.IsLockedOut));
			node.Add(new XElement(LastLockedOutDateNode, member.LastLockoutDate));
			node.Add(new XElement(LastLoginDateNode, member.LastLoginDate));
			node.Add(new XElement(LastPasswordChangeDateNode, member.LastPasswordChangeDate));
			if (!string.IsNullOrWhiteSpace(member.PasswordQuestion))
			{
				node.Add(new XElement(PasswordQuestionNode, member.PasswordQuestion));
			}
			if (!string.IsNullOrWhiteSpace(member.RawPasswordAnswerValue))
			{
				node.Add(new XElement(RawPasswordAnswerNode, member.RawPasswordAnswerValue));
			}
			var securityGroups = System.Web.Security.Roles.GetRolesForUser(member.Username);
			if (securityGroups.Any())
			{
				var groups = new XElement(GroupsNode);
				foreach (var group in System.Web.Security.Roles.GetRolesForUser(member.Username))
				{
					groups.Add(new XElement(GroupNode, group));
				}
				node.Add(groups);
			}

			var memberDto = Database.SingleOrDefault<MemberDto>(member.Id);
			if (memberDto != null && !string.IsNullOrWhiteSpace(memberDto.Password))
			{
				var cryptography = new Cryptography(member.Email + member.Name);
				node.Add(new XElement(PasswordNode, cryptography.Encrypt(memberDto.Password)));
			}

			foreach (var prop in member.Properties.Where(p => p != null && !p.Alias.StartsWith("umbracoMember")))
			{
				node.Add(CreateProperty(prop));
			}

			return node.Normalize();
		}

		private uSyncAction ExportMember(IMemberType memberType, IMember member)
		{
			if (memberType == null || member == null)
			{
				return uSyncAction.Fail("Member", typeof(IMember), "Member not set");
			}

			try
			{
				var node = Serialize(memberType, member);
				var attempt = SyncAttempt<XElement>.Succeed(member.Email, node, typeof(IMember), ChangeType.Export);
				var filename = Filename(memberType, member);
				uSyncIOHelper.SaveNode(attempt.Item, filename);
				return uSyncActionHelper<XElement>.SetAction(attempt, filename);
			}
			catch (Exception ex)
			{
				LogHelper.Warn<MemberHandler>($"Error saving Member {member.Email}: {ex.Message}");
				return uSyncAction.Fail(member.Email, typeof(IMember), ChangeType.Export, ex);
			}
		}

		private IEnumerable<uSyncAction> ExportFolder(IMemberType memberType)
		{
			var actions = new List<uSyncAction>();
			foreach (var member in _memberService.GetMembersByMemberType(memberType.Id))
			{
				actions.Add(ExportMember(memberType, member));
			}

			return actions;
		}

		public IEnumerable<uSyncAction> ExportAll(string folder)
		{
			var actions = new List<uSyncAction>();
			try
			{
				foreach (var memberType in _memberTypeService.GetAll())
				{
					actions.AddRange(ExportFolder(memberType));
				}
			}
			catch (Exception ex)
			{
				LogHelper.Error<MemberHandler>($"Export Failed ", ex);
			}
			return actions;
		}

		private string GetImportIds(PropertyType propType, string content)
		{
			var mapping = uSyncCoreContext.Instance.Configuration.Settings.ContentMappings
				.SingleOrDefault(x => x.EditorAlias == propType.PropertyEditorAlias);

			if (mapping != null)
			{
				LogHelper.Debug<Events>("Mapping Content Import: {0} {1}", () => mapping.EditorAlias, () => mapping.MappingType);

				IContentMapper mapper = ContentMapperFactory.GetMapper(mapping);

				if (mapper != null)
				{
					return mapper.GetImportValue(propType.DataTypeDefinitionId, content);
				}
			}

			return content;
		}

		private string GetImportXml(XElement parent)
		{
			var reader = parent.CreateReader();
			reader.MoveToContent();
			string xml = reader.ReadInnerXml();

			if (xml.StartsWith("<![CDATA["))
				return parent.Value;
			else
				return xml.Replace("&amp;", "&");
		}

		private bool? Deserialize(XElement node, bool force, out IMember member)
		{
			bool isNewMember = false;
			var email = node.Attribute(KeyAttribute);
			var name = node.Attribute(NameAttribute);
			var user = node.Attribute(UserAttribute);
			var memberType = node.Attribute(TypeAttribute);
			member = null;

			if (email == null || name == null || user == null || memberType == null)
			{
				LogHelper.Warn<MemberHandler>($"Error reading {node.Document.BaseUri}");
				return null;
			}

			member = _memberService.GetByEmail(email.Value);

			if (member == null)
			{
				member = _memberService.CreateMember(user.Value, email.Value, name.Value, MemberType(memberType.Value));
				isNewMember = true;
			}

			var groups = new List<string>();
			string password = null;
			foreach (var el in node.Elements())
			{
				switch (el.Name.LocalName)
				{
					case CommentsNode:
						member.Comments = el.Value;
						break;

					case FailedPasswordAttemptsNode:
						member.FailedPasswordAttempts = int.Parse(el.Value);
						break;

					case GroupsNode:
						foreach (var group in el.Elements())
						{
							groups.Add(el.Value);
						}
						break;

					case IsApprovedNode:
						member.IsApproved = bool.Parse(el.Value);
						break;

					case IsLockedOutNode:
						member.IsLockedOut = bool.Parse(el.Value);
						break;

					case LastLockedOutDateNode:
						member.LastLockoutDate = DateTime.Parse(el.Value);
						break;

					case LastLoginDateNode:
						member.LastLoginDate = DateTime.Parse(el.Value);
						break;

					case LastPasswordChangeDateNode:
						member.LastPasswordChangeDate = DateTime.Parse(el.Value);
						break;

					case PasswordQuestionNode:
						member.PasswordQuestion = el.Value;
						break;

					case PasswordNode:
						password = el.Value;
						break;

					case RawPasswordAnswerNode:
						member.RawPasswordAnswerValue = el.Value;
						break;

					default:
						var alias = el.Name.LocalName;
						var value = GetImportIds(member.Properties[alias].PropertyType, GetImportXml(el));
						member.SetValue(alias, value);
						break;
				}
			}

			_memberService.Save(member, true);

			if (isNewMember)
			{
				if (groups.Any())
				{
					System.Web.Security.Roles.AddUserToRoles(member.Username, groups.ToArray());
				}
			}
			else
			{
				var existingGroups = System.Web.Security.Roles.GetRolesForUser(member.Username);
				if (groups.Any() && !existingGroups.Any())
				{
					System.Web.Security.Roles.AddUserToRoles(member.Username, groups.ToArray());
				}
				else
				{
					if (groups.Any())
					{
						var groupsToAdd = groups.Where(x => !existingGroups.Any(y => x == y));
						if (groupsToAdd != null && groupsToAdd.Any())
						{
							System.Web.Security.Roles.AddUserToRoles(member.Username, groupsToAdd.ToArray());
						}
					}
					if (existingGroups.Any())
					{
						var groupsToRemove = existingGroups.Where(x => !groups.Any(y => x == y));
						if (groupsToRemove != null && groupsToRemove.Any())
						{
							System.Web.Security.Roles.RemoveUserFromRoles(member.Username, groupsToRemove.ToArray());
						}
					}
				}
			}

			if (password != null)
			{
				var memberDto = Database.SingleOrDefault<MemberDto>(member.Id);
				if (memberDto != null)
				{
					var cryptography = new Cryptography(email.Value + name.Value);
					memberDto.Password = cryptography.Decrypt(password);
					Database.Update(memberDto);
				}
				else
				{
					LogHelper.Warn<MemberHandler>($"Member {member.Id} doesn\'t exist in table cmsMember even after we have just saved it");
				}
			}

			return true;
		}

		private IEnumerable<uSyncAction> ImportFile(string file, bool force)
		{
			var actions = new List<uSyncAction>();
			var xml = XDocument.Load(file);

			foreach (var el in xml.Nodes().Where(x => x.NodeType == System.Xml.XmlNodeType.Element).Cast<XElement>().Where(x => x.Name == NodeName))
			{
				IMember member;
				var success = Deserialize(el, force, out member);
				if (success == true)
				{
					actions.Add(uSyncActionHelper<IMember>.SetAction(SyncAttempt<IMember>.Succeed(member.Email, member, ChangeType.Import), file));
				}
				else if (success == false)
				{
					actions.Add(uSyncActionHelper<IMember>.SetAction(SyncAttempt<IMember>.Fail(member.Email, member, ChangeType.Import), file));
				}
				else   // Must be null
				{
					//	We can't import this member, as its of a different type, silently ignore
				}
			}
			return actions;
		}

		private IEnumerable<uSyncAction> ImportFolder(string folder, bool force)
		{
			var actions = new List<uSyncAction>();
			if (Directory.Exists(folder))
			{
				foreach (var file in Directory.GetFiles(folder, FileFilter))
				{
					actions.AddRange(ImportFile(file, force));
				}

				foreach (var child in Directory.GetDirectories(folder))
				{
					actions.AddRange(ImportFolder(child, force));
				}

			}

			return actions;
		}

		public IEnumerable<uSyncAction> ImportAll(string folder, bool force)
		{
			try
			{
				return ImportFolder(Umbraco.Core.IO.IOHelper.MapPath(folder), force);
			}
			catch (Exception ex)
			{
				LogHelper.Error<MemberHandler>($"Import Failed ", ex);
			}
			return Enumerable.Empty<uSyncAction>();
		}

		// Return True, if the element matches an existing member with same properties, false if they don't match or don't exist, null if we can't import this because of different types
		private bool? CompareMember(XElement node)
		{
			var email = node.Attribute(KeyAttribute);
			var name = node.Attribute(NameAttribute);
			var user = node.Attribute(UserAttribute);
			var memberType = node.Attribute(TypeAttribute);

			if (email == null || name == null || user == null || memberType == null)
			{
				LogHelper.Warn<MemberHandler>($"Error reading {node.ToString()}");
				return null;
			}

			var existingMember = _memberService.GetByEmail(email.Value);

			if (existingMember == null || existingMember.ContentTypeAlias != memberType.Value || name.Value != existingMember.Name || user.Value != existingMember.Username)
			{
				return false;
			}

			var compare = Serialize(existingMember.ContentType, existingMember);

			return XNode.DeepEquals(node.Normalize(), compare);
		}

		public IEnumerable<uSyncAction> ReportFile(string file)
		{
			var actions = new List<uSyncAction>();
			var xml = XDocument.Load(file);

			foreach (var el in xml.Nodes().Where(x => x.NodeType == System.Xml.XmlNodeType.Element).Cast<XElement>().Where(x => x.Name == NodeName))
			{
				actions.Add(uSyncActionHelper<IMember>.ReportAction(CompareMember(el) == false, el.Attribute(KeyAttribute).Value));
			}
			return actions;
		}

		public IEnumerable<uSyncAction> ReportFolder(string folder)
		{
			var actions = new List<uSyncAction>();
			if (Directory.Exists(folder))
			{
				foreach (var file in Directory.GetFiles(folder, FileFilter))
				{
					actions.AddRange(ReportFile(file));
				}

				foreach (var child in Directory.GetDirectories(folder))
				{
					actions.AddRange(ReportFolder(child));
				}
			}

			return actions;
		}

		public IEnumerable<uSyncAction> Report(string folder)
		{
			try
			{
				return ReportFolder(Umbraco.Core.IO.IOHelper.MapPath(folder));
			}
			catch (Exception ex)
			{
				LogHelper.Error<MemberHandler>($"Report Failed ", ex);
			}
			return Enumerable.Empty<uSyncAction>();

		}
	}
}
