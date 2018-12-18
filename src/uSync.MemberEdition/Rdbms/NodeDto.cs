using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace uSync.MemberEdition.Rdbms
{
    [TableName("umbracoNode")]
    [PrimaryKey("id")]
    [ExplicitColumns]
    internal class NodeDto
    {
        public const int NodeIdSeed = 1050;

        [Column("id")]
        [PrimaryKeyColumn(Name = "PK_structure", IdentitySeed = NodeIdSeed)]
        public int NodeId { get; set; }
	}
}
