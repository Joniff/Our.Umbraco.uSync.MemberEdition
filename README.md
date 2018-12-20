# Our.Umbraco.uSync.MemberEdition

Currently this allows the importing and exporting of members from any Umbraco 7.5.14 or later.

It doesn't install itself into uSync config files. You will need to edit uSyncBackOffice.Config

  > Add This line to all &lt;Handlers> sections.
  
      <HandlerConfig Name="Deploy:MemberHandler" Enabled="true"/>

  >Also make sure 
  
      <HandlerConfig Name="Deploy:MemberTypeHandler" Enabled="true"/>
      
  >is enabled

- - - -


**Limitations**

*  It will not create MemberGroups, if a Member Group doesn't exist on importing, it will be siliently ignored.
*  It will import/export password hashes, if the hashing mechanism is different on destination installation, then the password will be invalid/corrupt
*  It uses email as the unqiue identifier, duplicate emails will error.
*  I wrote this for my own need, it does not have extensive testing

**Positives**

* Contains an extra level of encryption ontop of the existing password hashes, so regardless of what system of hashing is present, an extra level is provided for all sync files
* Reuses existing uSync property mapping to convert property types.

