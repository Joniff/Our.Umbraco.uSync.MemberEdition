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
		
		private const string NodeName = "member";
		private const string KeyAttribute = "email";
		private const string TypeAttribute = "type";
		private const string AliasAttribute = "alias";
		private const string NameAttribute = "name";
		private const string UserAttribute = "user";

		private const string CommentsNode = "comments";
		private const string FailedPasswordAttemptsNode = "failedPasswordAttempts";
		private const string GroupsNode = "groups";
		private const string GroupNode = "group";
		private const string IsApprovedNode = "isApproved";
		private const string IsLockedOutNode = "isLockedOut";
		private const string LastLockedOutDateNode = "lastLockedOutDate";
		private const string LastLoginDateNode = "lastLoginDate";
		private const string LastPasswordChangeDateNode = "lastPasswordChangeDate";
		private const string PasswordQuestionNode = "passwordQuestion";
		private const string PasswordNode = "password";
		private const string RawPasswordAnswerNode = "rawPasswordAnswer";

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


		private SyncAttempt<XElement> Serialize(IMemberType memberType, IMember member)
		{
			var node = new XElement(NodeName, 
				new XAttribute(KeyAttribute, member.Email),
                new XAttribute(TypeAttribute, memberType.Alias),
				new XAttribute(NameAttribute, member.Name),
				new XAttribute(UserAttribute, member.Username)
			);

			//	Do custom properties first, as accessing standard properties creates corresponding pseudo custom properties (in version 7.5.4 at least)
            foreach (var prop in member.Properties.Where(p => p != null))
			{
				node.Add(CreateProperty(prop));
			}

			node.Add(new XElement(CommentsNode, member.Comments));
			node.Add(new XElement(FailedPasswordAttemptsNode, member.FailedPasswordAttempts));
			node.Add(new XElement(IsApprovedNode, member.IsApproved));
			node.Add(new XElement(IsLockedOutNode, member.IsLockedOut));
			node.Add(new XElement(LastLockedOutDateNode, member.LastLockoutDate));
			node.Add(new XElement(LastLoginDateNode, member.LastLoginDate));
			node.Add(new XElement(LastPasswordChangeDateNode, member.LastPasswordChangeDate));
			node.Add(new XElement(PasswordQuestionNode, member.PasswordQuestion));
			node.Add(new XElement(RawPasswordAnswerNode, member.RawPasswordAnswerValue));

			var groups = new XElement(GroupsNode);
			foreach (var group in System.Web.Security.Roles.GetRolesForUser(member.Username))
			{
				groups.Add(new XElement(GroupNode, group));
			}
			node.Add(groups);

			var memberDto = Database.SingleOrDefault<MemberDto>(member.Id);
			if (memberDto != null && !string.IsNullOrWhiteSpace(memberDto.Password))
			{
				node.Add(new XElement(PasswordNode, memberDto.Password));
			}
		
			return SyncAttempt<XElement>.Succeed(member.Email, node, typeof(IMember), ChangeType.Export);

		}

		private uSyncAction ExportMember(IMemberType memberType, IMember member)
		{
			if (memberType == null || member == null)
			{
				return uSyncAction.Fail("Member", typeof(IMember), "Member not set");
			}

			try
            {
				var attempt = Serialize(memberType, member);
                var filename = Filename(memberType, member);
                uSyncIOHelper.SaveNode(attempt.Item, filename);
                return uSyncActionHelper<XElement>.SetAction(attempt, filename);
			}
            catch(Exception ex)
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
			foreach (var memberType in _memberTypeService.GetAll())
			{
				actions.AddRange(ExportFolder(memberType));
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

		private bool? Deserialize(XElement node, bool force, out IMember newMember)
		{
			var email = node.Attribute(KeyAttribute);
			var name = node.Attribute(NameAttribute);
			var user = node.Attribute(UserAttribute);
			var memberType = node.Attribute(TypeAttribute);
			newMember = null;

			if (email == null || name == null || user == null || memberType == null)
			{
                LogHelper.Warn<MemberHandler>($"Error reading {node.Document.BaseUri}");
				return null;
			}				

			var member = _memberService.GetByEmail(email.Value);

			if (member == null)
			{
				member = _memberService.CreateMember(user.Value, email.Value, name.Value, MemberType(memberType.Value));
			}

			var groups = new List<string>();
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
						member.RawPasswordValue = el.Value;
						break;

					case RawPasswordAnswerNode:
						member.RawPasswordAnswerValue = el.Value;
						break;

					default:
						var alias = el.Attribute(AliasAttribute).Value;
						var propType = el.Attribute(TypeAttribute).Value;

						var value = GetImportIds(member.Properties[propType].PropertyType, GetImportXml(el));
						member.SetValue(alias, value);
						break;
				}
			}

			_memberService.Save(member, true);
			if (groups.Any())
			{
				System.Web.Security.Roles.AddUserToRoles(member.Username, groups.ToArray());
			}
			return true;
		}

		private IEnumerable<uSyncAction> ImportFile(string file, bool force)
		{
            var actions = new List<uSyncAction>();
            var xml = XElement.Load(file);

			foreach (var el in xml.Nodes().Where(x => x.NodeType == System.Xml.XmlNodeType.Element).Cast<XElement>())
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
                foreach(var file in Directory.GetFiles(folder, FileExtension))
                {
                    actions.AddRange(ImportFile(file, force));
                }

                foreach(var child in Directory.GetDirectories(folder))
                {
                    actions.AddRange(ImportFolder(child, force));
                }

            }

            return actions;
		}

		public IEnumerable<uSyncAction> ImportAll(string folder, bool force)
		{
            return ImportFolder(Umbraco.Core.IO.IOHelper.MapPath(folder), force);
		}

		// Return True, if the elment matches an existing member with same properties, false if they don't match or don't exist, null if we can't import this because of different types
		private bool? CompareMember(XElement node)
		{
			var email = node.Attribute(KeyAttribute);
			var name = node.Attribute(NameAttribute);
			var user = node.Attribute(UserAttribute);
			var memberType = node.Attribute(TypeAttribute);

			if (email == null || name == null || user == null || memberType == null)
			{
                LogHelper.Warn<MemberHandler>($"Error reading {node.Document.BaseUri}");
				return null;
			}				

			var existingMember = _memberService.GetByEmail(email.Value);

			if (existingMember == null || existingMember.ContentTypeAlias != memberType.Value || name.Value != existingMember.Name || user.Value != existingMember.Username)
			{
				return false;
			}

			foreach (var el in node.Elements())
			{
				var alias = el.Attribute(AliasAttribute).Value;
				var propType = el.Attribute(TypeAttribute).Value;

				var match = existingMember.Properties.FirstOrDefault(x => x.Alias == alias);
				if (match == null)
				{
					return false;
				}

				if (match.PropertyType.Alias != propType)
				{
					return null;	//	We can't convert property types
				}

				var newProperty = CreateProperty(match);
				if (newProperty.Value != el.Value)
				{
					return false;
				}
			}

			return true;
		}

        public IEnumerable<uSyncAction> ReportFile(string file)
        {
            var actions = new List<uSyncAction>();
            var xml = XElement.Load(file);

			foreach (var el in xml.Nodes().Where(x => x.NodeType == System.Xml.XmlNodeType.Element).Cast<XElement>())
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
                foreach(var file in Directory.GetFiles(folder, FileExtension))
                {
                    actions.AddRange(ReportFile(file));
                }

                foreach(var child in Directory.GetDirectories(folder))
                {
                    actions.AddRange(ReportFolder(child));
                }

            }

            return actions;
		}

		public IEnumerable<uSyncAction> Report(string folder)
        {
            return ReportFolder(Umbraco.Core.IO.IOHelper.MapPath(folder));
        }
    }
}
