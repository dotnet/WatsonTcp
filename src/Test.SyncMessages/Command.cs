namespace Test.SyncMessages
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public class Command
    {
        public CommandTypeEnum CommandType { get; set; } = CommandTypeEnum.Echo;

        public int Int { get; set; } = 0;

        public string Data { get; set; } = null;

        public Command()
        {

        }
    }
}
