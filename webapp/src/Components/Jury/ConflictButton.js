import React from 'react';
import classNames from 'classnames';
import ModalDialog from '../ModalDialog';
import WikiLink from '../WikiLink';
import { findMarkOf, calcMark, isConflict } from './../../jury';
import { withTranslation } from './../../translate';

class ConflictButton extends React.Component {
   constructor(props) {
      super(props);
      this.state = {
         firstTime: localStorage.getItem('fountainFirstConflict') === null,
         showDialog: false,
      };
   }

   onClick() {
      if (this.state.firstTime) {
         localStorage.setItem('fountainFirstConflict', 'false');
      }
      this.setState({ firstTime: false, showDialog: true });
   }

   isConflict() {
      const article = this.props.article;
      return findMarkOf(article.marks) !== undefined && isConflict(this.props.editathon, article);
   }

   formatNumber(...args) {
      return this.props.translation.translate('formatNumber', ...args);
   }

   renderMark(parts) {
      const details = [];
      for (const p in parts) {
         const v = parts[p];
         details.push(<dt key={'dt' + p}>{v && this.formatNumber(v, { forcePlus: true }) + ' ' || ''}</dt>);
         details.push(<dd key={'dd' + p}>{p}</dd>);
      }
      return details;
   }

   render() {
      if (!this.isConflict()) return null;
      const { editathon: { jury, marks: marksConfig, wiki }, article: { marks } } = this.props;

      const all = jury.map(j => ({
         jury: j,
         mark: findMarkOf(marks, j),
      })).filter(m => m.mark).map(m => ({
         jury: m.jury,
         parts: calcMark(m.mark.marks, marksConfig).parts,
         comment: m.mark.comment,
      }));

      return <div className='ConflictButton'>
         <button className={classNames({ firstTime: this.state.firstTime })} onClick={() => this.onClick()} />
         <ModalDialog isOpen={this.state.showDialog} tryClose={() => this.setState({ showDialog: false })}>
            <table>
               <tbody>
                  {all.map((m, i) => <tr key={i}>
                     <td className='jury'>
                        <WikiLink to={'User_talk:' + m.jury} wiki={wiki} target='_blank' />
                     </td>
                     <td className='mark'>
                        <dl>{this.renderMark(m.parts)}</dl>
                     </td>
                     <td className='comment'>{m.comment}</td>
                  </tr>)}
               </tbody>
            </table>
         </ModalDialog>
      </div>
   }
}

export default withTranslation(ConflictButton);
