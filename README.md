# Our.Umbraco.uSync.MemberEdition

Currently this allows the importing and exporting of members from any Umbraco 7.5.14 or later. There is a list of things it can't do.

It doesn't install itself into uSync config files. You will need to edit uSyncBackOffice.Config

  Add This line to all &lt;Handlers> sections.
      &lt;HandlerConfig Name="Deploy:MemberHandler" Enabled="true"/>

  Also make sure 
    &lt;HandlerConfig Name="Deploy:MemberTypeHandler" Enabled="true"/>
  Is enabled


Limitations

*  It will not create MemberGroups, if a Member Group doesn't exist on importing, it will be siliently ignored.
*  It will import/export password hashes, if the hashing mechanism is different on destination installation, then the password will be invalid/corrupt
*  It uses email as the unqiue identifier, duplicate emails will error.
*  I wrote this for my own need, it does not have extensive testing

Positives

* Contains an extra level of encryption ontop of the existing password hashes, so regardless of what system of hashing is present, an extra level is provided for all sync files
* Reuses existing uSync property mapping to convert property types.

