using System;

namespace AsyncScrolling
{
    public class PolicyTemplateModel : ModelBase
    {
        private long _userNum;
        private string _userID;
        private string _userName;
        private bool _userChecked;

        public PolicyTemplateModel()
        {
            base.AddProperty<long, PolicyTemplateModel>("UserNum");
            base.AddProperty<string, PolicyTemplateModel>("UserID");
            base.AddProperty<string, PolicyTemplateModel>("UserName");
            base.AddProperty<bool, PolicyTemplateModel>("UserChecked");
        }

        public long UserNum
        {
            get { return _userNum; }
            set
            {
                _userNum = value;
                base.SetPropertyValue<long>("UserNum", value);
            }
        }

        public string UserID
        {
            get { return _userID; }
            set
            {
                _userID = value;
                base.SetPropertyValue<string>("UserID", value);
            }
        }

        public string UserName
        {
            get { return _userName; }
            set
            {
                _userName = value;
                base.SetPropertyValue<string>("UserName", value);
            }
        }

        public bool UserChecked
        {
            get { return _userChecked; }
            set
            {
                _userChecked = value;
                base.SetPropertyValue<bool>("UserChecked", value);
            }
        }
    }
}
